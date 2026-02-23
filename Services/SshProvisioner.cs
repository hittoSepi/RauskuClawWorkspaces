using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace RauskuClaw.Services
{
    public sealed class SshProvisioner
    {
        public async Task RunCommandAsync(string host, int port, string user, string privateKeyPath, string command, CancellationToken ct)
        {
            await Task.Yield();

            using var keyFile = new Renci.SshNet.PrivateKeyFile(privateKeyPath);
            using var client = new Renci.SshNet.SshClient(host, port, user, keyFile);

            client.Connect();
            var cmd = client.CreateCommand(command);
            cmd.Execute();
            if (cmd.ExitStatus != 0)
                throw new Exception($"SSH command failed ({cmd.ExitStatus}): {cmd.Error}");
            client.Disconnect();
        }
    }

}
