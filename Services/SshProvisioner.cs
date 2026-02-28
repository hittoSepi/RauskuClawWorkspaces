using System;
using System.Threading;
using System.Threading.Tasks;

namespace RauskuClaw.Services
{
    public sealed class SshProvisioner
    {
        private readonly ISshConnectionFactory _sshConnectionFactory;

        public SshProvisioner(ISshConnectionFactory? sshConnectionFactory = null)
        {
            _sshConnectionFactory = sshConnectionFactory ?? new SshConnectionFactory();
        }

        public async Task RunCommandAsync(string host, int port, string user, string privateKeyPath, string command, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Yield();

            using var client = _sshConnectionFactory.ConnectSshClient(host, port, user, privateKeyPath);
            var cmd = client.CreateCommand(command);
            cmd.Execute();
            if (cmd.ExitStatus != 0)
            {
                throw new Exception($"SSH command failed ({cmd.ExitStatus}): {cmd.Error}");
            }

            client.Disconnect();
        }
    }
}
