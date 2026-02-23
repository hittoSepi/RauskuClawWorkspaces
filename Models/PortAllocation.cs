namespace RauskuClaw.Models
{
    /// <summary>
    /// Port allocation for a workspace's forwarded ports.
    /// </summary>
    public class PortAllocation
    {
        public int Ssh { get; set; }
        public int Api { get; set; }
        public int UiV2 { get; set; }
        public int UiV1 { get; set; }
        public int Qmp { get; set; }
        public int Serial { get; set; }
    }
}
