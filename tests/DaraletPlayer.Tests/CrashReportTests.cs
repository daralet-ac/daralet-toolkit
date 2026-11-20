using DaraletPlayer.Feed;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DaraletPlayer.Tests {
    [TestClass]
    public class CrashReportTests {
        // as Windows wrote it for the AnimData::UnPack crash (November 2026)
        private const string Entry = "Faulting application name: acclient.exe, version: 0.0.11.6096, time stamp: 0x557a956c\r\n"
            + "Faulting module name: acclient.exe, version: 0.0.11.6096, time stamp: 0x557a956c\r\n"
            + "Exception code: 0xc0000005\r\nFault offset: 0x00126a66\r\nFaulting process id: 0x7F54\r\n";

        private static readonly (uint, string)[] Functions = {
            (0x526A00u, "Earlier__Thing"), (0x526A50u, "AnimData__UnPack"), (0x526B00u, "Later__Thing"),
        };

        [TestMethod]
        public void ReadsTheGamesCrash() {
            var report = CrashReport.Read(Entry, false);
            Assert.AreEqual("acclient.exe", report.Module);
            Assert.AreEqual(0x526A66u, report.Address);
            Assert.AreEqual("0xc0000005", report.Code);
            StringAssert.Contains(report.Describe(Functions), "AnimData::UnPack +0x16");
        }

        [TestMethod]
        public void LeavesOtherProgramsAlone() {
            Assert.IsNull(CrashReport.Read(Entry.Replace("acclient.exe", "other.exe"), false));
        }

        [TestMethod]
        public void ACrashInAnotherModuleSaysItsOffset() {
            var report = CrashReport.Read(Entry.Replace("Faulting module name: acclient.exe", "Faulting module name: d3d9.dll"),
                false);
            Assert.AreEqual(0u, report.Address);
            StringAssert.Contains(report.Describe(Functions), "d3d9.dll at offset 0x126A66");
        }

        [TestMethod]
        public void FarFromAnyKnownFunctionIsUnnamed() {
            Assert.IsNull(CrashReport.NameOf(0x600000, Functions));
        }
    }
}
