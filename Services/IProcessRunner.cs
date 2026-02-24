using System;
using System.Diagnostics;

namespace RauskuClaw.Services
{
    public sealed class ProcessRunResult
    {
        public int ExitCode { get; init; }
        public string StandardOutput { get; init; } = string.Empty;
        public string StandardError { get; init; } = string.Empty;
    }

    public interface IProcessRunner
    {
        ProcessRunResult Run(ProcessStartInfo processStartInfo);
    }

    public sealed class ProcessRunner : IProcessRunner
    {
        public ProcessRunResult Run(ProcessStartInfo processStartInfo)
        {
            using var process = Process.Start(processStartInfo);
            if (process == null)
            {
                throw new InvalidOperationException($"Failed to start {processStartInfo.FileName}.");
            }

            var stdOut = process.StandardOutput.ReadToEnd();
            var stdErr = process.StandardError.ReadToEnd();
            process.WaitForExit();

            return new ProcessRunResult
            {
                ExitCode = process.ExitCode,
                StandardOutput = stdOut,
                StandardError = stdErr
            };
        }
    }
}
