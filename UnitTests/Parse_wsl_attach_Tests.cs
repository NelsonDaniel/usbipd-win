﻿// SPDX-FileCopyrightText: 2022 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-2.0-only

using System.CommandLine;
using Usbipd.Automation;

namespace UnitTests;

using ExitCode = Program.ExitCode;

[TestClass]
sealed class Parse_wsl_attach_Tests
    : ParseTestBase
{
    static readonly BusId TestBusId = BusId.Parse("3-42");
    static readonly VidPid TestHardwareId = VidPid.Parse("0123:cdef");
    const string TestDistribution = "Test Distribution";

    [TestMethod]
    public void BusIdSuccess()
    {
        var mock = CreateMock();
        mock.Setup(m => m.WslAttach(It.Is<BusId>(busId => busId == TestBusId), false, null,
            It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Returns(Task.FromResult(ExitCode.Success));

        Test(ExitCode.Success, mock, "wsl", "attach", "--busid", TestBusId.ToString());
    }

    [TestMethod]
    public void BusIdSuccessWithAutoAttach()
    {
        var mock = CreateMock();
        mock.Setup(m => m.WslAttach(It.Is<BusId>(busId => busId == TestBusId), true, null,
            It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Returns(Task.FromResult(ExitCode.Success));

        Test(ExitCode.Success, mock, "wsl", "attach", "--busid", TestBusId.ToString(), "--auto-attach");
    }

    [TestMethod]
    public void BusIdSuccessWithDistribution()
    {
        var mock = CreateMock();
        mock.Setup(m => m.WslAttach(It.Is<BusId>(busId => busId == TestBusId), false, It.Is<string>(distribution => distribution == TestDistribution),
            It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Returns(Task.FromResult(ExitCode.Success));

        Test(ExitCode.Success, mock, "wsl", "attach", "--busid", TestBusId.ToString(), "--distribution", TestDistribution);
    }

    [TestMethod]
    public void BusIdFailure()
    {
        var mock = CreateMock();
        mock.Setup(m => m.WslAttach(It.Is<BusId>(busId => busId == TestBusId), false, null,
            It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Returns(Task.FromResult(ExitCode.Failure));

        Test(ExitCode.Failure, mock, "wsl", "attach", "--busid", TestBusId.ToString());
    }

    [TestMethod]
    public void BusIdCanceled()
    {
        var mock = CreateMock();
        mock.Setup(m => m.WslAttach(It.Is<BusId>(busId => busId == TestBusId), false, null,
            It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Throws<OperationCanceledException>();

        Test(ExitCode.Canceled, mock, "wsl", "attach", "--busid", TestBusId.ToString());
    }

    [TestMethod]
    public void HardwareIdSuccess()
    {
        var mock = CreateMock();
        mock.Setup(m => m.WslAttach(It.Is<VidPid>(vidPid => vidPid == TestHardwareId), false, null,
            It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Returns(Task.FromResult(ExitCode.Success));

        Test(ExitCode.Success, mock, "wsl", "attach", "--hardware-id", TestHardwareId.ToString());
    }

    [TestMethod]
    public void HardwareIdFailure()
    {
        var mock = CreateMock();
        mock.Setup(m => m.WslAttach(It.Is<VidPid>(vidPid => vidPid == TestHardwareId), false, null,
            It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Returns(Task.FromResult(ExitCode.Failure));

        Test(ExitCode.Failure, mock, "wsl", "attach", "--hardware-id", TestHardwareId.ToString());
    }

    [TestMethod]
    public void HardwareIdCanceled()
    {
        var mock = CreateMock();
        mock.Setup(m => m.WslAttach(It.Is<VidPid>(vidPid => vidPid == TestHardwareId), false, null,
            It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Throws<OperationCanceledException>();

        Test(ExitCode.Canceled, mock, "wsl", "attach", "--hardware-id", TestHardwareId.ToString());
    }

    [TestMethod]
    public void Help()
    {
        Test(ExitCode.Success, "wsl", "attach", "--help");
    }

    [TestMethod]
    public void OptionMissing()
    {
        Test(ExitCode.ParseError, "wsl", "attach");
    }

    [TestMethod]
    public void BusIdAndHardwareId()
    {
        Test(ExitCode.ParseError, "wsl", "attach", "--busid", TestBusId.ToString(), "--hardware-id", TestHardwareId.ToString());
    }

    [TestMethod]
    public void BusIdArgumentMissing()
    {
        Test(ExitCode.ParseError, "wsl", "attach", "--busid");
    }

    [TestMethod]
    public void HardwareIdArgumentMissing()
    {
        Test(ExitCode.ParseError, "wsl", "attach", "--hardware-id");
    }

    [TestMethod]
    public void BusIdArgumentInvalid()
    {
        Test(ExitCode.ParseError, "wsl", "attach", "--busid", "not-a-busid");
    }

    [TestMethod]
    public void HardwareIdArgumentInvalid()
    {
        Test(ExitCode.ParseError, "wsl", "attach", "--hardware-id", "not-a-hardware-id");
    }

    [TestMethod]
    public void StrayArgument()
    {
        Test(ExitCode.ParseError, "wsl", "attach", "stray-argument");
    }
}
