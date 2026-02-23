using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace RauskuClaw.Services
{
    public sealed class SeedIsoService
    {
        public void CreateSeedIso(string isoPath, string userData, string metaData, string? networkConfig = null)
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(isoPath));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            if (File.Exists(isoPath)) File.Delete(isoPath);

            var builder = new DiscUtils.Iso9660.CDBuilder
            {
                UseJoliet = true,
                VolumeIdentifier = "CIDATA"
            };

            builder.AddFile("user-data", System.Text.Encoding.UTF8.GetBytes(userData));
            builder.AddFile("meta-data", System.Text.Encoding.UTF8.GetBytes(metaData));

            if (!string.IsNullOrWhiteSpace(networkConfig))
                builder.AddFile("network-config", System.Text.Encoding.UTF8.GetBytes(networkConfig));

            builder.Build(isoPath);
        }
    }

}
