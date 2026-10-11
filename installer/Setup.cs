// Apex Drive installer for Windows. Build: see installer/build.ps1. The game files are inside this exe as the resource "payload.zip".
// It unpacks them to %LOCALAPPDATA%\Programs\ApexDrive, adds Start menu and desktop shortcuts and an entry in Settings > Apps.
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Win32;

static class Setup
{
    const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\ApexDrive";

    [STAThread]
    static int Main(string[] args)
    {
        bool quiet = args.Length > 0 && args[0].ToLowerInvariant() == "/quiet";
        string dest = Environment.GetEnvironmentVariable("APEX_INSTALL_DIR");
        if (string.IsNullOrEmpty(dest))
            dest = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Path.Combine("Programs", "ApexDrive"));
        bool test = Environment.GetEnvironmentVariable("APEX_TEST") == "1";
        bool noLaunch = Environment.GetEnvironmentVariable("APEX_NO_LAUNCH") == "1";

        Application.EnableVisualStyles();
        if (!quiet)
        {
            var ask = MessageBox.Show("Install Apex Drive on this computer?\n\nIt will be installed to:\n" + dest, "Apex Drive Setup", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (ask != DialogResult.Yes) return 0;
        }

        Form progress = null;
        try
        {
            if (!quiet)
            {
                progress = new Form { Text = "Apex Drive Setup", Width = 380, Height = 110, StartPosition = FormStartPosition.CenterScreen, FormBorderStyle = FormBorderStyle.FixedDialog, ControlBox = false };
                progress.Controls.Add(new Label { Text = "Installing Apex Drive...", AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 11f) });
                progress.Show();
                Application.DoEvents();
            }

            // an earlier copy of the game is replaced (only if it really is one)
            if (File.Exists(Path.Combine(dest, "ApexDrive.exe"))) Directory.Delete(dest, true);
            Directory.CreateDirectory(dest);
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip"))
            using (var zip = new ZipArchive(s, ZipArchiveMode.Read))
                zip.ExtractToDirectory(dest);

            string exe = Path.Combine(dest, "ApexDrive.exe");
            if (!File.Exists(exe)) throw new FileNotFoundException("ApexDrive.exe was not unpacked.");

            string uninstall = Path.Combine(dest, "uninstall.cmd");
            File.WriteAllText(uninstall,
                "@echo off\r\n" +
                "setlocal\r\n" +
                "set \"DEST=%~dp0\"\r\n" +
                "if \"%DEST:~-1%\"==\"\\\" set \"DEST=%DEST:~0,-1%\"\r\n" +
                "echo Removing Apex Drive...\r\n" +
                "powershell -NoProfile -ExecutionPolicy Bypass -Command \"foreach ($d in @([Environment]::GetFolderPath('Desktop'), [Environment]::GetFolderPath('Programs'))) { $f = $d + '\\Apex Drive.lnk'; if (Test-Path $f) { Remove-Item $f } }\"\r\n" +
                "reg delete \"HKCU\\" + UninstallKey + "\" /f >nul 2>nul\r\n" +
                "start \"\" /b cmd /c \"ping 127.0.0.1 -n 3 >nul & rmdir /s /q \"%DEST%\"\"\r\n" +
                "exit /b 0\r\n");

            if (!test)
            {
                MakeShortcut(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), exe, dest);
                MakeShortcut(Environment.GetFolderPath(Environment.SpecialFolder.Programs), exe, dest);
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(UninstallKey))
                {
                    k.SetValue("DisplayName", "Apex Drive");
                    k.SetValue("DisplayIcon", exe);
                    k.SetValue("InstallLocation", dest);
                    k.SetValue("Publisher", "kooooneth");
                    k.SetValue("UninstallString", "\"" + uninstall + "\"");
                    k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                    k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                }
            }
            if (progress != null) progress.Close();
            if (!quiet)
            {
                var go = MessageBox.Show("Apex Drive is installed.\n\nThere is a shortcut on your desktop and in the Start menu.\n\nStart it now?", "Apex Drive Setup", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                if (go == DialogResult.Yes && !noLaunch) Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = dest });
            }
            else if (!noLaunch) Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = dest });
            return 0;
        }
        catch (Exception e)
        {
            if (progress != null) progress.Close();
            MessageBox.Show("The install failed:\n\n" + e.Message, "Apex Drive Setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    static void MakeShortcut(string folder, string exe, string workDir)
    {
        try
        {
            Type t = Type.GetTypeFromProgID("WScript.Shell");
            object shell = Activator.CreateInstance(t);
            object link = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { Path.Combine(folder, "Apex Drive.lnk") });
            Type lt = link.GetType();
            lt.InvokeMember("TargetPath", BindingFlags.SetProperty, null, link, new object[] { exe });
            lt.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, link, new object[] { workDir });
            lt.InvokeMember("IconLocation", BindingFlags.SetProperty, null, link, new object[] { exe });
            lt.InvokeMember("Description", BindingFlags.SetProperty, null, link, new object[] { "Apex Drive" });
            lt.InvokeMember("Save", BindingFlags.InvokeMethod, null, link, null);
        }
        catch (Exception) { }
    }
}
