using System;

namespace RauskuClaw.Services
{
    public interface IStartupProgressReporter
    {
        void ReportStage(IProgress<string>? progress, string stage, string state, string message, Action<string> appendLog);

        void ReportLog(IProgress<string>? progress, string message, Action<string> appendLog);

        string WithStartupReason(string fallbackReason, string message);
    }
}
