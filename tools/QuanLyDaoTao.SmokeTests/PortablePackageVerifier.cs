using System.Diagnostics;
using System.IO;
using System.Text;
using QuanLyDaoTao.Core;

namespace QuanLyDaoTao.SmokeTests;

internal static class PortablePackageVerifier
{
    internal static int Verify(string sourceExe, string? sourceDatabase = null)
    {
        var temporaryRoot = Path.GetFullPath(Path.GetTempPath());
        var directory = Path.GetFullPath(Path.Combine(temporaryRoot, "MediTrain-PortableTest-" + Guid.NewGuid().ToString("N")));
        Process? process = null;
        try
        {
            Directory.CreateDirectory(directory);
            var isolatedExe = Path.Combine(directory, "MediTrain.exe");
            File.Copy(Path.GetFullPath(sourceExe), isolatedExe);
            if (Directory.GetFiles(directory).Length != 1) throw new InvalidOperationException("Test folder must contain only the copied EXE.");
            var database = sourceDatabase is null ? Path.Combine(directory, "data", "test.db") : Path.Combine(directory, "Data", "training.db");
            int? expectedEmployees = null, expectedSupplements = null;
            if (sourceDatabase is not null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(database)!);
                File.Copy(Path.GetFullPath(sourceDatabase), database);
                File.SetLastWriteTimeUtc(database, new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc));
                var expected = new TrainingRepository(sourceDatabase);
                expectedEmployees = expected.GetEmployees().Count;
                expectedSupplements = expected.GetSupplements().Count;
            }
            var bundleDirectory = Path.Combine(directory, "bundle");
            var start = new ProcessStartInfo(isolatedExe)
            {
                WorkingDirectory = directory, UseShellExecute = false,
                CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden
            };
            if (sourceDatabase is null)
            {
                start.ArgumentList.Add("--database");
                start.ArgumentList.Add(database);
            }
            start.Environment["DOTNET_ROOT"] = Path.Combine(directory, "no-shared-dotnet");
            start.Environment["DOTNET_ROOT_X64"] = start.Environment["DOTNET_ROOT"];
            start.Environment["DOTNET_MULTILEVEL_LOOKUP"] = "0";
            start.Environment["DOTNET_BUNDLE_EXTRACT_BASE_DIR"] = bundleDirectory;
            process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start portable EXE.");
            var stopwatch = Stopwatch.StartNew();
            var verified = false;
            while (stopwatch.Elapsed < TimeSpan.FromSeconds(30))
            {
                if (process.HasExited) throw new InvalidOperationException($"Portable EXE exited before startup verification: {process.ExitCode}");
                process.Refresh();
                if (File.Exists(database) && new FileInfo(database).Length > 0 && process.MainWindowHandle != IntPtr.Zero)
                {
                    var modules = process.Modules.Cast<ProcessModule>().ToArray();
                    bool Bundled(string name) => modules.Any(module => string.Equals(module.ModuleName, name, StringComparison.OrdinalIgnoreCase)
                        && Path.GetFullPath(module.FileName).StartsWith(bundleDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
                    // Modern Windows single-file hosts can statically link CoreCLR into the EXE.
                    var clrModules = modules.Where(module => string.Equals(module.ModuleName, "coreclr.dll", StringComparison.OrdinalIgnoreCase)).ToArray();
                    var runtimeIsBundled = clrModules.Length > 0 ? Bundled("coreclr.dll")
                        : modules.Any(module => string.Equals(Path.GetFullPath(module.FileName), isolatedExe, StringComparison.OrdinalIgnoreCase));
                    if (runtimeIsBundled && Bundled("e_sqlite3.dll") && Bundled("wpfgfx_cor3.dll"))
                    {
                        if (!process.MainWindowTitle.StartsWith("MediTrain", StringComparison.Ordinal))
                            throw new InvalidOperationException("Application did not reach the main window: " + process.MainWindowTitle);
                        using var stream = new FileStream(database, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        var header = new byte[16]; stream.ReadExactly(header);
                        if (Encoding.ASCII.GetString(header) != "SQLite format 3\0") throw new InvalidOperationException("Invalid SQLite database.");
                        if (sourceDatabase is not null)
                        {
                            if (File.GetLastWriteTimeUtc(database).Year == 2001) throw new InvalidOperationException("Application did not initialize the adjacent Data/training.db without command-line arguments.");
                            var saved = new TrainingRepository(database);
                            if (saved.GetEmployees().Count != expectedEmployees || saved.GetSupplements().Count != expectedSupplements)
                                throw new InvalidOperationException("Packaged employee/certificate data did not survive startup.");
                            Console.WriteLine($"PASS: no-argument startup used adjacent Data/training.db, retaining {expectedEmployees} employees and {expectedSupplements} supplemental records.");
                        }
                        verified = true;
                        Console.WriteLine(sourceDatabase is null ? "PASS: one copied EXE started in an otherwise empty folder." : "PASS: portable EXE and adjacent database started from a new isolated folder.");
                        Console.WriteLine("PASS: .NET runtime is contained in the EXE; SQLite and WPF native libraries loaded from its extracted bundle.");
                        Console.WriteLine("PASS: application reached MediTrain main window and created a valid SQLite database.");
                        Console.WriteLine($"Startup: {stopwatch.Elapsed.TotalSeconds:0.0}s; EXE: {new FileInfo(isolatedExe).Length:N0} bytes.");
                        break;
                    }
                }
                Thread.Sleep(200);
            }
            if (!verified) throw new TimeoutException("Portable EXE startup did not complete within 30 seconds.");
            return 0;
        }
        catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
        finally
        {
            if (process is { HasExited: false }) { process.Kill(entireProcessTree: true); process.WaitForExit(); }
            process?.Dispose();
            // Only this test's unique directory inside the resolved temporary root can be removed.
            if (directory.StartsWith(temporaryRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                && Path.GetFileName(directory).StartsWith("MediTrain-PortableTest-", StringComparison.Ordinal)
                && Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
