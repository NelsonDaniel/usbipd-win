﻿// SPDX-FileCopyrightText: 2022 Frans van Dorsselaer
// SPDX-FileCopyrightText: Microsoft Corporation
//
// SPDX-License-Identifier: GPL-2.0-only

using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Logging;

using static UsbIpServer.ConsoleTools;
using ExitCode = UsbIpServer.Program.ExitCode;

namespace UsbIpServer
{
    interface ICommandHandlers
    {
        public Task<ExitCode> Bind(BusId busId, IConsole console, CancellationToken cancellationToken);
        public Task<ExitCode> License(IConsole console, CancellationToken cancellationToken);
        public Task<ExitCode> List(IConsole console, CancellationToken cancellationToken);
        public Task<ExitCode> Server(string[] args, IConsole console, CancellationToken cancellationToken);
        public Task<ExitCode> Unbind(BusId busId, IConsole console, CancellationToken cancellationToken);
        public Task<ExitCode> Unbind(Guid guid, IConsole console, CancellationToken cancellationToken);
        public Task<ExitCode> UnbindAll(IConsole console, CancellationToken cancellationToken);

        public Task<ExitCode> WslAttach(BusId busId, string? distribution, string? usbipPath, IConsole console, CancellationToken cancellationToken);
        public Task<ExitCode> WslDetach(BusId busId, IConsole console, CancellationToken cancellationToken);
        public Task<ExitCode> WslDetachAll(IConsole console, CancellationToken cancellationToken);
        public Task<ExitCode> WslList(IConsole console, CancellationToken cancellationToken);
    }

    class CommandHandlers : ICommandHandlers
    {
        static bool CheckWriteAccess()
        {
            if (!RegistryUtils.HasWriteAccess())
            {
                ReportError("Access denied.");
                return false;
            }
            return true;
        }

        static bool CheckServerRunning()
        {
            if (!UsbIpServer.Server.IsServerRunning())
            {
                ReportError("Server is currently not running.");
                return false;
            }
            return true;
        }

        public Task<ExitCode> License(IConsole console, CancellationToken cancellationToken)
        {
            // 70 leads (approximately) to the GPL default.
            var width = console.IsOutputRedirected ? 70 : Console.WindowWidth;
            foreach (var line in Wrap($"{Program.Product} {GitVersionInformation.MajorMinorPatch}\n"
                + $"{Program.Copyright}\n"
                + "\n"
                + "This program is free software: you can redistribute it and/or modify "
                + "it under the terms of the GNU General Public License as published by "
                + "the Free Software Foundation, version 2.\n"
                + "\n"
                + "This program is distributed in the hope that it will be useful, "
                + "but WITHOUT ANY WARRANTY; without even the implied warranty of "
                + "MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the "
                + "GNU General Public License for more details.\n"
                + "\n"
                + "You should have received a copy of the GNU General Public License "
                + "along with this program. If not, see <https://www.gnu.org/licenses/>.\n"
                , width))
            {
                console.WriteLine(line);
            }
            return Task.FromResult(ExitCode.Success);
        }

        public async Task<ExitCode> List(IConsole console, CancellationToken cancellationToken)
        {
            var connectedDevices = await ExportedDevice.GetAll(cancellationToken);
            var persistedDevices = RegistryUtils.GetPersistedDevices(connectedDevices);
            console.WriteLine("Present:");
            console.WriteLine($"{"BUSID",-5}  {"DEVICE",-60}  STATE");
            foreach (var device in connectedDevices)
            {
                // NOTE: Strictly speaking, both Bus and Port can be > 99. If you have one of those, you win a prize!
                console.WriteLine($@"{device.BusId,-5}  {device.Description.Truncate(60),-60}  {
                    (RegistryUtils.IsDeviceShared(device) ? RegistryUtils.IsDeviceAttached(device) ? "Attached" : "Shared" : "Not shared")}");
            }
            console.Write("\n");
            console.WriteLine("Persisted:");
            console.WriteLine($"{"GUID",-38}  {"BUSID",-5}  DEVICE");
            foreach (var device in persistedDevices)
            {
                console.WriteLine($"{device.Guid,-38:B}  {device.BusId,-5}  {device.Description.Truncate(60),-60}");
            }
            ReportServerRunning();
            return ExitCode.Success;
        }

        static async Task<ExitCode> Bind(BusId busId, bool wslAttach, IConsole console, CancellationToken cancellationToken)
        {
            var connectedDevices = await ExportedDevice.GetAll(cancellationToken);
            var device = connectedDevices.Where(x => x.BusId == busId).SingleOrDefault();
            if (device is null)
            {
                ReportError($"There is no compatible device with busid '{busId}'.");
                return ExitCode.Failure;
            }
            if (RegistryUtils.IsDeviceShared(device))
            {
                // Not an error, just let the user know they just executed a no-op.
                if (!wslAttach)
                {
                    ReportInfo($"Connected device with busid '{busId}' was already shared.");
                }
                return ExitCode.Success;
            }
            if (!CheckWriteAccess())
            {
                return ExitCode.AccessDenied;
            }
            RegistryUtils.ShareDevice(device, device.Description);
            return ExitCode.Success;
        }

        public Task<ExitCode> Bind(BusId busId, IConsole console, CancellationToken cancellationToken)
        {
            return Bind(busId, false, console, cancellationToken);
        }

        public async Task<ExitCode> Server(string[] args, IConsole console, CancellationToken cancellationToken)
        {
            // Pre-conditions that may fail due to user mistakes. Fail gracefully...

            if (!CheckWriteAccess())
            {
                return ExitCode.AccessDenied;
            }

            using var mutex = new Mutex(true, UsbIpServer.Server.SingletonMutexName, out var createdNew);
            if (!createdNew)
            {
                ReportError("Another instance is already running.");
                return ExitCode.Failure;
            }

            // From here on, the server should run without error. Any further errors (exceptions) are probably bugs...

            using var host = Host.CreateDefaultBuilder()
                .UseWindowsService()
                .ConfigureAppConfiguration((context, builder) =>
                {
                    var defaultConfig = new Dictionary<string, string>();
                    if (WindowsServiceHelpers.IsWindowsService())
                    {
                        // EventLog defaults to Warning, which is OK for .NET components,
                        //      but we want to specifically log Information from our own component.
                        defaultConfig.Add($"Logging:EventLog:LogLevel:{nameof(UsbIpServer)}", "Information");
                    }
                    else
                    {
                        // When not running as a Windows service, do not spam the EventLog.
                        defaultConfig.Add("Logging:EventLog:LogLevel:Default", "None");
                    }
                    // set the above as defaults
                    builder.AddInMemoryCollection(defaultConfig);
                    // allow overrides from the environment
                    builder.AddEnvironmentVariables();
                    // allow overrides from the command line
                    builder.AddCommandLine(args);
                })
                .ConfigureLogging((context, logging) =>
                {
                    logging.AddEventLog(settings =>
                    {
                        settings.SourceName = Program.Product;
                    });
                })
                .ConfigureServices((hostContext, services) =>
                {
                    services.AddHostedService<Server>();
                    services.AddScoped<ClientContext>();
                    services.AddScoped<ConnectedClient>();
                    services.AddScoped<AttachedClient>();
                })
                .Build();

            await host.RunAsync(cancellationToken);
            return ExitCode.Success;
        }

        public async Task<ExitCode> Unbind(BusId busId, IConsole console, CancellationToken cancellationToken)
        {
            var connectedDevices = await ExportedDevice.GetAll(cancellationToken);
            var device = connectedDevices.Where(x => x.BusId == busId).SingleOrDefault();
            if (device is null)
            {
                ReportError($"There is no compatible device with busid '{busId}'.");
                return ExitCode.Failure;
            }
            if (!RegistryUtils.IsDeviceShared(device))
            {
                // Not an error, just let the user know they just executed a no-op.
                ReportInfo($"Connected device with busid '{busId}' was already not shared.");
                return ExitCode.Success;
            }
            if (!CheckWriteAccess())
            {
                return ExitCode.AccessDenied;
            }
            RegistryUtils.StopSharingDevice(device);
            return ExitCode.Success;
        }

        public Task<ExitCode> Unbind(Guid guid, IConsole console, CancellationToken cancellationToken)
        {
            if (!RegistryUtils.GetPersistedDeviceGuids().Contains(guid))
            {
                // Not an error, just let the user know they just executed a no-op.
                ReportInfo($"There is no persisted device with guid '{guid:B}'.");
                return Task.FromResult(ExitCode.Success);
            }
            if (!CheckWriteAccess())
            {
                return Task.FromResult(ExitCode.AccessDenied);
            }
            RegistryUtils.StopSharingDevice(guid);
            return Task.FromResult(ExitCode.Success);
        }

        public Task<ExitCode> UnbindAll(IConsole console, CancellationToken cancellationToken)
        {
            if (!CheckWriteAccess())
            {
                return Task.FromResult(ExitCode.AccessDenied);
            }
            RegistryUtils.StopSharingAllDevices();
            return Task.FromResult(ExitCode.Success);
        }

        static async Task<WslDistributions?> GetDistributionsAsync(CancellationToken cancellationToken)
        {
            var distributions = await WslDistributions.CreateAsync(cancellationToken);
            if (distributions is null)
            {
                ReportError($"Windows Subsystem for Linux version 2 is not available. See {WslDistributions.InstallWslUrl}.");
            }
            return distributions;
        }

        public async Task<ExitCode> WslAttach(BusId busId, string? distribution, string? usbipPath, IConsole console, CancellationToken cancellationToken)
        {
            if (await GetDistributionsAsync(cancellationToken) is not WslDistributions distros)
            {
                return ExitCode.Failure;
            }

            if (!CheckServerRunning())
            {
                return ExitCode.Failure;
            }

            // Make sure the distro is running before we attach. While WSL is capable of
            // starting on the fly when wsl.exe is invoked, that will cause confusing behavior
            // where we might attach a USB device to WSL, then immediately detach it when the
            // WSL VM is shutdown shortly afterwards.
            var distroData = distribution is not null ? distros.LookupByName(distribution) : distros.DefaultDistribution;

            // The order of the following checks is important, as later checks can only succeed if earlier checks already passed.

            // 1) Distro must exist

            if (distroData is null)
            {
                ReportError(distribution is not null
                    ? $"The WSL distribution '{distribution}' does not exist."
                    : "No default WSL distribution exists."
                );
                return ExitCode.Failure;
            }

            // 2) Distro must be correct WSL version

            switch (distroData.Version)
            {
                case 1:
                    ReportError($"The specified WSL distribution is using WSL 1, but WSL 2 is required. Learn how to upgrade at {WslDistributions.SetWslVersionUrl}.");
                    return ExitCode.Failure;
                case 2:
                    // Supported
                    break;
                default:
                    ReportError($"The specified WSL distribution is using unsupported WSL {distroData.Version}, but WSL 2 is required.");
                    return ExitCode.Failure;
            }

            // 3) Distro must be running

            if (!distroData.IsRunning)
            {
                ReportError($"The specified WSL distribution is not running.");
                return ExitCode.Failure;
            }

            // 4) Host must be reachable.
            //    This check only makes sense if at least one WSL 2 distro is running, which is ensured by earlier checks.

            if (distros.HostAddress is null)
            {
                // This would be weird: we already know that a WSL 2 instance is running.
                // Maybe the virtual switch does not have 'WSL' in the name?
                ReportError("The local IP address for the WSL virtual switch could not be found.");
                return ExitCode.Failure;
            }

            // 5) Distro must have connectivity.
            //    This check only makes sense if the host is reachable, which is ensured by earlier checks.

            if (distroData.IPAddress is null)
            {
                ReportError($"The specified WSL distribution cannot be reached via the WSL virtual switch; try restarting the WSL distribution.");
                return ExitCode.Failure;
            }

            var bindResult = await Bind(busId, true, console, cancellationToken);
            if (bindResult != ExitCode.Success)
            {
                ReportError($"Failed to bind device with BUSID '{busId}'.");
                return ExitCode.Failure;
            }

            var path = usbipPath ?? "usbip";
            var wslResult = await ProcessUtils.RunUncapturedProcessAsync(
                WslDistributions.WslPath,
                (distribution is not null ? new[] { "--distribution", distribution } : Enumerable.Empty<string>()).Concat(
                    new[] { "--", "sudo", path, "attach", $"--remote={distros.HostAddress}", $"--busid={busId}" }),
                cancellationToken);
            if (wslResult != 0)
            {
                ReportError($"Failed to attach device with BUSID '{busId}'.");
                return ExitCode.Failure;
            }

            return ExitCode.Success;
        }

        public Task<ExitCode> WslDetach(BusId busId, IConsole console, CancellationToken cancellationToken)
        {
            return Unbind(busId, console, cancellationToken);
        }

        public Task<ExitCode> WslDetachAll(IConsole console, CancellationToken cancellationToken)
        {
            return UnbindAll(console, cancellationToken);
        }

        public async Task<ExitCode> WslList(IConsole console, CancellationToken cancellationToken)
        {
            if (await GetDistributionsAsync(cancellationToken) is not WslDistributions distros)
            {
                return ExitCode.Failure;
            }

            var connectedDevices = await ExportedDevice.GetAll(CancellationToken.None);

            Console.WriteLine($"{"BUSID",-5}  {"DEVICE",-60}  STATE");
            foreach (var device in connectedDevices)
            {
                var isAttached = RegistryUtils.IsDeviceAttached(device);
                var address = RegistryUtils.GetDeviceAddress(device);
                var distro = address is not null ? distros.LookupByIPAddress(address)?.Name : null;
                var state = isAttached ? ("Attached" + (distro is not null ? $" - {distro}" : string.Empty)) : "Not attached";
                var description = ConsoleTools.Truncate(device.Description, 60);

                Console.WriteLine($"{device.BusId,-5}  {description,-60}  {state}");
            }
            ReportServerRunning();
            return ExitCode.Success;
        }
    }
}
