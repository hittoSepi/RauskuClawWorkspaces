using System;
using System.Collections.Generic;
using System.Windows;

namespace RauskuClaw
{
    /// <summary>
    /// Queues startup messages to be displayed after the main window is ready.
    /// </summary>
    internal sealed class StartupMessageQueue
    {
        private readonly List<Action> _actions = new();
        private bool _hasShownMessages;

        public void QueueInfo(string title, string message)
        {
            _actions.Add(() => GUI.Views.ThemedDialogWindow.ShowInfo(
                Application.Current?.MainWindow,
                title,
                message));
        }

        public void ShowQueuedMessages()
        {
            if (_hasShownMessages || _actions.Count == 0)
            {
                return;
            }

            _hasShownMessages = true;

            foreach (var action in _actions)
            {
                try
                {
                    action();
                }
                catch
                {
                    // Best-effort display - continue to next message
                }
            }
        }
    }
}
