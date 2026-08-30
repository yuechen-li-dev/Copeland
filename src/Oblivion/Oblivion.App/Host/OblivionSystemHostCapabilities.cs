using System.Diagnostics;

namespace Oblivion.App;

public static class OblivionSystemHostCapabilities
{
    public static OblivionLocalHostCapabilities Create()
    {
        return new OblivionLocalHostCapabilities(
            OpenPath: OpenPath,
            CopyText: CopyText);
    }

    private static OblivionHostCapabilityResult OpenPath(OblivionOpenPathCapabilityRequest request)
    {
        try
        {
            Process.Start(new ProcessStartInfo(request.ResolvedPath)
            {
                UseShellExecute = true,
            });
            return new OblivionHostCapabilityResult(
                true,
                $"Opened {request.TargetKind.ToString().ToLowerInvariant()} '{request.ResolvedPath}'.");
        }
        catch (Exception exception)
        {
            return new OblivionHostCapabilityResult(
                false,
                $"The local host could not open '{request.ResolvedPath}': {exception.Message}",
                "OBLIVION-HOST-OPEN-FAILED");
        }
    }

    private static OblivionHostCapabilityResult CopyText(OblivionCopyTextCapabilityRequest request)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new OblivionHostCapabilityResult(
                false,
                "Clipboard copy is not available from this local host.",
                "OBLIVION-HOST-CAPABILITY-UNAVAILABLE");
        }

        try
        {
            ProcessStartInfo startInfo = new("clip.exe")
            {
                UseShellExecute = false,
                RedirectStandardInput = true,
                CreateNoWindow = true,
            };
            using Process process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("clip.exe did not start.");
            process.StandardInput.Write(request.Text);
            process.StandardInput.Close();
            if (!process.WaitForExit(5000))
            {
                process.Kill(entireProcessTree: true);
                return new OblivionHostCapabilityResult(
                    false,
                    "Clipboard host did not complete within five seconds.",
                    "OBLIVION-HOST-COPY-FAILED");
            }
            if (process.ExitCode != 0)
            {
                return new OblivionHostCapabilityResult(
                    false,
                    $"Clipboard host exited with code {process.ExitCode}.",
                    "OBLIVION-HOST-COPY-FAILED");
            }

            return new OblivionHostCapabilityResult(true, "Copied the resolved source path to the clipboard.");
        }
        catch (Exception exception)
        {
            return new OblivionHostCapabilityResult(
                false,
                $"The local host could not copy text: {exception.Message}",
                "OBLIVION-HOST-COPY-FAILED");
        }
    }
}
