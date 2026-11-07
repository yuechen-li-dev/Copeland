using System.Diagnostics;

namespace Marionette.Skyrim.App;

/// <summary>Serial character-menu and travel orchestration on the ordinary host loop.
/// Persistent identity stays in the existing roster; a pending menu/trip is transient.</summary>
public sealed class SkyrimCharacterSwitchController
{
    private SkyrimWorldOwnerRuntime? owner;
    private EmbodiedCharacter? target;
    private EmbodiedCharacter? previous;
    private string phase = "idle";
    private bool menuRequested;
    private bool rollingBack;
    private ulong lastMenuSequence;
    private bool observedMenuSequence;
    private ulong prompt;
    private ulong menuGeneration;
    private int page;
    private EmbodiedCharacter[] menuCharacters = [];
    private long deadline;
    private long nextAttempt;
    private (Guid Character, ulong Generation)? dormant;
    private string notification = "";

    public bool IsSwitching => phase is "prepare" or "waiting";

    public void RequestMenu() => menuRequested = true;

    public void Select(SkyrimHostContext context, Guid character)
    {
        if (phase != "idle") throw new ArgumentException("character_switch_busy");
        owner = context.WorldOwner ?? throw new NotSupportedException("character_roster_unavailable");
        EmbodimentState roster = owner.ReadEmbodiment() ?? throw new ArgumentException("character_roster_empty");
        target = roster.Characters.SingleOrDefault(entry => entry.CharacterId == character)
            ?? throw new ArgumentException("embodiment_character_unknown");
        previous = roster.Selected;
        rollingBack = false;
        phase = "prepare";
        deadline = Stopwatch.GetTimestamp() + 90 * Stopwatch.Frequency;
        nextAttempt = 0;
        dormant = null;
        Trace("requested", target.CharacterId.ToString());
    }

    public void Visit(SkyrimHostContext context, Marionette.Skyrim.SkyrimPlacedActorOrigin origin, SkyrimCharacterLocation hint)
    {
        if (phase != "idle") throw new ArgumentException("character_switch_busy");
        hint.Validate();
        owner = context.WorldOwner ?? throw new NotSupportedException("character_roster_unavailable");
        previous = owner.ReadEmbodiment()?.Selected;
        target = new(Guid.NewGuid(), origin.PluginName, origin.LocalFormId, LastLocation: hint);
        rollingBack = false;
        phase = "prepare";
        deadline = Stopwatch.GetTimestamp() + 90 * Stopwatch.Frequency;
        nextAttempt = 0;
        Trace("visit", origin.StableKey);
    }

    internal async ValueTask<bool> TickAsync(SkyrimHostContext context, bool ready, CancellationToken token)
    {
        SkyrimBodyControl? bodies = context.Bodies;
        if (bodies is null || context.WorldOwner is null) return false;
        if (owner is not null && owner != context.WorldOwner)
        {
            Reset();
            Trace("cancelled", "world_replaced");
        }
        ulong sequence = bodies.Observation.MenuRequestSequence;
        if (!observedMenuSequence)
        {
            observedMenuSequence = true;
            lastMenuSequence = sequence;
        }
        else if (sequence != lastMenuSequence)
        {
            lastMenuSequence = sequence;
            if (phase == "idle") menuRequested = true;
        }
        if (phase == "menu")
        {
            if (context.State.WorldGeneration != menuGeneration)
            {
                Reset();
                Trace("cancelled", "menu_world_changed");
                return true;
            }
            WorldNpcResult response = await context.QuestUiAsync("query", token).ConfigureAwait(false);
            QuestUiObservation menu = response.QuestUi ?? throw new InvalidDataException("character_menu_receipt_missing");
            if (menu.PromptId != prompt)
            {
                Reset();
                Trace("cancelled", "menu_prompt_changed");
                return true;
            }
            if (menu.Choice < 0)
            {
                if (menu.PromptOpen == false)
                {
                    Reset();
                    Trace("cancelled", "menu_closed");
                }
                return true;
            }
            int start = page * 6;
            int count = Math.Min(6, menuCharacters.Length - start);
            if (menu.Choice < count)
            {
                Guid selected = menuCharacters[start + menu.Choice].CharacterId;
                phase = "idle";
                Select(context, selected);
            }
            else if (menu.Choice == count && menuCharacters.Length > 6)
            {
                page = (page + 1) % ((menuCharacters.Length + 5) / 6);
                phase = "idle";
                menuRequested = true;
            }
            else
            {
                Reset();
                Trace("cancelled", "user");
            }
            return true;
        }
        if (IsSwitching)
        {
            if (Stopwatch.GetTimestamp() > deadline)
            {
                Fail(context, "destination_timeout");
                return true;
            }
            if (!ready || Stopwatch.GetTimestamp() < nextAttempt) return true;
            nextAttempt = Stopwatch.GetTimestamp() + Stopwatch.Frequency;
            try
            {
                if (phase == "prepare") await PrepareAsync(context, token).ConfigureAwait(false);
                else await AcquireAsync(context, token).ConfigureAwait(false);
            }
            catch (SkyrimBodyControlException exception) when (exception.IsDeferred)
            {
                // A timed-out travel has an uncertain outcome. Observe/acquire;
                // never resend that mutation because a receipt was lost.
                if (phase == "prepare") phase = "waiting";
                Trace("deferred", exception.Reason);
            }
            catch (SkyrimBodyControlException exception)
            {
                if (phase == "waiting" && exception.Reason is "body_reference_unavailable" or "body_actor_ineligible" or
                    "body_world_not_ready" or "body_actor_outside_region")
                {
                    Trace("waiting", exception.Reason);
                }
                else Fail(context, exception.Reason);
            }
            return true;
        }
        if (!ready) return menuRequested;
        if (notification.Length != 0)
        {
            await context.QuestUiAsync("notify", token, notification).ConfigureAwait(false);
            notification = "";
        }
        if (menuRequested)
        {
            await OpenMenuAsync(context, token).ConfigureAwait(false);
            return true;
        }
        // Version-one saves without a location keep M8's original nearby resume.
        // New saves can warm the selected body's last cell before fresh acquisition.
        if (bodies.Lease is null && context.WorldOwner.ReadEmbodiment()?.Selected is { LastLocation: not null } selectedBody &&
            dormant != (selectedBody.CharacterId, context.State.WorldGeneration))
        {
            Select(context, selectedBody.CharacterId);
            previous = null;
            return true;
        }
        return false;
    }

    private async ValueTask OpenMenuAsync(SkyrimHostContext context, CancellationToken token)
    {
        owner = context.WorldOwner!;
        menuCharacters = owner.ReadEmbodiment()?.Characters ?? [];
        if (menuCharacters.Length == 0)
        {
            menuRequested = false;
            await context.QuestUiAsync("notify", token, "No playable characters have been registered.").ConfigureAwait(false);
            return;
        }
        page = Math.Clamp(page, 0, (menuCharacters.Length - 1) / 6);
        int start = page * 6;
        int count = Math.Min(6, menuCharacters.Length - start);
        var labels = new List<string>();
        var descriptions = new List<string> { "Switch character" };
        for (int index = 0; index < count; index++)
        {
            EmbodiedCharacter character = menuCharacters[start + index];
            string availability = "";
            try
            {
                BodyCharacterInfo info = await context.Bodies!.DescribeCharacterAsync(character.Origin, context.State.WorldGeneration, token).ConfigureAwait(false);
                if (info.UnavailableReason.Length != 0) availability = " [unavailable]";
                else if (info.Location is not null) character = owner.RegisterCharacter(info);
                if (!info.Loaded && availability.Length == 0) availability = " [away]";
            }
            catch (SkyrimBodyControlException exception) when (exception.Reason == "body_reference_unavailable")
            {
                availability = character.LastLocation is null ? " [unavailable]" : " [away]";
            }
            catch (SkyrimBodyControlException exception) when (exception.IsDeferred || exception.Reason is "body_world_not_ready" or "body_world_generation_changed")
            {
                Trace("deferred", exception.Reason);
                return; // No prompt has been issued; readonly metadata can be refreshed later.
            }
            menuCharacters[start + index] = character;
            string name = string.IsNullOrWhiteSpace(character.Name) ? $"Character {character.LocalFormId:X6}" : character.Name;
            string location = string.IsNullOrWhiteSpace(character.LocationName) ? "Location unknown" : character.LocationName;
            bool current = owner.ReadEmbodiment()?.SelectedCharacterId == character.CharacterId;
            name = name.Replace('\n', ' ').Replace('\r', ' ');
            location = location.Replace('\n', ' ').Replace('\r', ' ');
            descriptions.Add($"{index + 1}. {name[..Math.Min(name.Length, 26)]} - {location[..Math.Min(location.Length, 26)]}" + (current ? " (current)" : availability));
            labels.Add((index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        if (menuCharacters.Length > 6) labels.Add("Next page");
        labels.Add("Cancel");
        prompt = checked((ulong)Stopwatch.GetTimestamp());
        menuGeneration = context.State.WorldGeneration;
        menuRequested = false;
        phase = "menu";
        try
        {
            await context.QuestUiAsync("choices", token, string.Join('\n', descriptions), prompt, string.Join('\n', labels)).ConfigureAwait(false);
        }
        catch (SkyrimWorldCommandDeferredException exception)
        {
            // Query this prompt ID next tick. Never replay a possibly-created menu.
            Trace("deferred", exception.Message);
        }
        Trace("menu", $"prompt={prompt} page={page} entries={count}");
    }

    private async ValueTask PrepareAsync(SkyrimHostContext context, CancellationToken token)
    {
        EmbodiedCharacter destination = target!;
        BodyCharacterInfo? info = null;
        try
        {
            info = await context.Bodies!.DescribeCharacterAsync(destination.Origin, context.State.WorldGeneration, token).ConfigureAwait(false);
        }
        catch (SkyrimBodyControlException exception) when (exception.Reason == "body_reference_unavailable" && destination.LastLocation is not null)
        {
            // A nonpersistent placed reference may not exist in memory until its cell loads.
        }
        if (info is not null)
        {
            if (info.UnavailableReason.Length != 0)
            {
                Fail(context, info.UnavailableReason);
                return;
            }
            if (info.Location is null)
            {
                Fail(context, "body_destination_unavailable");
                return;
            }
            destination = context.WorldOwner!.RegisterCharacter(info);
            target = destination;
            if (info.Loaded)
            {
                try
                {
                    await AcquireAsync(context, token, info).ConfigureAwait(false);
                    return;
                }
                catch (SkyrimBodyControlException exception) when (exception.Reason == "body_actor_outside_region") { }
            }
        }
        phase = "waiting"; // Set before issuing the one-shot mutation, including receipt loss.
        Trace("travel", destination.Origin.StableKey);
        await context.Bodies!.TravelToCharacterAsync(destination.Origin, destination.LastLocation,
            context.State.WorldGeneration, token).ConfigureAwait(false);
    }

    private async ValueTask AcquireAsync(SkyrimHostContext context, CancellationToken token, BodyCharacterInfo? prepared = null)
    {
        EmbodiedCharacter destination = target!;
        if (context.Bodies!.Lease?.FormId is uint current &&
            context.Bodies.Observation.PluginName.Length != 0 &&
            new Marionette.Skyrim.SkyrimPlacedActorOrigin(context.Bodies.Observation.PluginName,
                context.Bodies.Observation.LocalFormId) == destination.Origin)
        {
            Trace("completed", $"character={destination.CharacterId} form={current} unchanged=true");
            Reset();
            return;
        }
        BodyCharacterInfo info = prepared ?? await context.Bodies.DescribeCharacterAsync(destination.Origin, context.State.WorldGeneration, token).ConfigureAwait(false);
        if (info.UnavailableReason.Length != 0)
        {
            Fail(context, info.UnavailableReason);
            return;
        }
        if (!info.Loaded)
        {
            Trace("waiting", "body_actor_not_loaded");
            return;
        }
        var actor = new LoadedActorBody(context.State.WorldGeneration, info.FormId);
        BodyControlObservation acquired = await context.AcquireBodyAsync(actor, context.Bodies.Lease is not null, token).ConfigureAwait(false);
        Trace(rollingBack ? "recovered" : "completed", $"character={context.WorldOwner!.ReadEmbodiment()!.SelectedCharacterId} form={acquired.FormId} cell={acquired.CellFormId} lease={acquired.LeaseId}");
        Reset();
    }

    private void Fail(SkyrimHostContext context, string reason)
    {
        Trace("failed", reason);
        notification = "Could not switch to that character.";
        if (!rollingBack && context.Bodies!.Lease is null && previous?.LastLocation is not null)
        {
            target = previous;
            rollingBack = true;
            phase = "prepare";
            deadline = Stopwatch.GetTimestamp() + 90 * Stopwatch.Frequency;
            nextAttempt = 0;
            Trace("rollback", previous.CharacterId.ToString());
            return;
        }
        if (context.WorldOwner?.ReadEmbodiment()?.SelectedCharacterId is Guid selected)
            dormant = (selected, context.State.WorldGeneration);
        Reset();
    }

    private void Reset()
    {
        phase = "idle";
        target = previous = null;
        owner = null;
        menuRequested = false;
        rollingBack = false;
    }

    private static void Trace(string stage, string details) => Console.WriteLine($"SKYRIM_CHARACTER_SWITCH phase={stage} {details}");
}
