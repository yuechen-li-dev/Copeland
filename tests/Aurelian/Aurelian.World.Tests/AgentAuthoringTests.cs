using Aurelian.World.Agents;
using Xunit;

namespace Aurelian.World.Tests;

public sealed class AgentAuthoringTests
{
    [Fact]
    public void CharacterObjectAndCreatureShareTypedCreationInIdentityOrder()
    {
        AgentSpawn<int>[] declarations =
        [
            new("player", "Runner", 3, AgentTemplate.Character("runner", AgentControl.Human)),
            new("beacon", "Beacon", 1, AgentTemplate.Object("collectible")),
            new("creature", "Stalker", 2, AgentTemplate.Creature("stalker")),
        ];
        var agents = AgentAuthoring.CreateBatch(declarations, [], _ => { }, spawn => new State(spawn.Placement));
        Assert.Equal(["beacon", "creature", "player"], agents.Select(agent => agent.Id));
        Assert.Equal([AgentKind.Object, AgentKind.Creature, AgentKind.Character], agents.Select(agent => agent.Template.Kind));
        Assert.Equal([1, 2, 3], agents.Select(agent => agent.State.Value));
    }

    [Fact]
    public void InvalidBatchDoesNotInvokeStateFactoryOrPublishAgents()
    {
        int calls = 0;
        AgentSpawn<int>[] declarations =
        [new("a", "A", 1, AgentTemplate.Object("object")), new("b", "B", -1, AgentTemplate.Creature("creature"))];
        Assert.Throws<InvalidDataException>(() => AgentAuthoring.CreateBatch(declarations, [], position =>
        {
            if (position < 0)
            {
                throw new InvalidDataException("Blocked placement.");
            }
        }, spawn =>
        {
            calls++;
            return new State(spawn.Placement);
        }));
        Assert.Equal(0, calls);
    }

    [Fact]
    public void DuplicateAndExistingIdentitiesAndInvalidKindsFailBeforeCreation()
    {
        var spawn = new AgentSpawn<int>("a", "A", 1, AgentTemplate.Object("object"));
        Assert.Throws<InvalidDataException>(() => AgentAuthoring.CreateBatch([spawn, spawn], [], _ => { }, _ => 1));
        Assert.Throws<InvalidDataException>(() => AgentAuthoring.CreateBatch([spawn], ["a"], _ => { }, _ => 1));
        Assert.Throws<InvalidDataException>(() => AgentAuthoring.ValidateTemplate(new("object", AgentKind.Object, AgentControl.Human)));
        Assert.Throws<InvalidDataException>(() => AgentAuthoring.ValidateHealth(13, 12));
    }

    private sealed record State(int Value);
}
