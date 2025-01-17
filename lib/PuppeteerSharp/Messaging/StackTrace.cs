namespace PuppeteerSharp.Messaging
{
    internal class StackTrace
    {
        public string Description { get; set; }

        public ConsoleMessageLocation[] CallFrames { get; set; }

        public StackTrace Parent { get; set; }

        public string ParentId { get; set; }
    }
}
