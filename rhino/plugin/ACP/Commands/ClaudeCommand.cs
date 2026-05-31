namespace RhMcp;

public sealed class ClaudeCommand : AgentCommand
{
    public override string EnglishName => "Claude";

    private protected override IAgent CreateAgent() => new ClaudeCliAgent();
}
