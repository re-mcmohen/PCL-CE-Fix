using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCL.Core.App.Essentials;

namespace PCL.Core.Test;

[TestClass]
public class UpdateHelperTest
{
    private string _directory = null!;
    private string Source => Path.Combine(_directory, "new.exe");
    private string Target => Path.Combine(_directory, "launcher.exe");

    [TestInitialize]
    public void Initialize()
    {
        _directory = Path.Combine(Path.GetTempPath(), "pcl-update-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Source, "new executable");
        File.WriteAllText(Target, "old executable");
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(_directory, recursive: true);

    [TestMethod]
    public void ReplacesExistingExecutableAndVerifiesInstalledHash()
    {
        Assert.IsNull(UpdateHelper.Replace(Source, Target, UpdateHelper.ComputeSha256(Source)));
        Assert.AreEqual("new executable", File.ReadAllText(Target));
        Assert.AreEqual(2, Directory.GetFiles(_directory).Length);
    }

    [TestMethod]
    public void InstallsWhenTargetIsMissing()
    {
        File.Delete(Target);
        Assert.IsNull(UpdateHelper.Replace(Source, Target));
        Assert.AreEqual("new executable", File.ReadAllText(Target));
    }

    [TestMethod]
    public void WrongHashPreservesOldExecutable()
    {
        Assert.IsInstanceOfType<InvalidDataException>(UpdateHelper.Replace(Source, Target, new string('0', 64)));
        Assert.AreEqual("old executable", File.ReadAllText(Target));
    }

    [TestMethod]
    public void MissingSourcePreservesOldExecutable()
    {
        File.Delete(Source);
        Assert.IsNotNull(UpdateHelper.Replace(Source, Target));
        Assert.AreEqual("old executable", File.ReadAllText(Target));
    }

    [TestMethod]
    public void RejectsReplacingSourceWithItself()
    {
        Assert.IsInstanceOfType<ArgumentException>(UpdateHelper.Replace(Source, Source));
        Assert.AreEqual("new executable", File.ReadAllText(Source));
    }

    [TestMethod]
    public async Task TemporaryWindowsLockRetriesUntilReplacementSucceeds()
    {
        var locked = new FileStream(Target, FileMode.Open, FileAccess.Read, FileShare.None);
        var replace = Task.Run(() => UpdateHelper.Replace(Source, Target, retryTimeout: TimeSpan.FromSeconds(5)));
        try
        {
            await Task.Delay(450);
            Assert.IsFalse(replace.IsCompleted, "Replacement must wait while the target is locked");
        }
        finally { locked.Dispose(); }
        Assert.IsNull(await replace);
        Assert.AreEqual("new executable", File.ReadAllText(Target));
    }

    [TestMethod]
    public void PersistentLockFailsWithoutDeletingOldExecutable()
    {
        using (var locked = new FileStream(Target, FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.IsNotNull(UpdateHelper.Replace(Source, Target, retryTimeout: TimeSpan.FromMilliseconds(200)));
        Assert.AreEqual("old executable", File.ReadAllText(Target));
        Assert.AreEqual(2, Directory.GetFiles(_directory).Length);
    }
}
