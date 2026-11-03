namespace Aurelian.Graphics.FramePlans;

/// <summary>Replays the published tape and rejects invalid artifacts before native recording.</summary>
public static class FramePlanVerifier
{
    public static void Verify(FramePlan plan)
    {
        if (plan.Format != FramePlan.CurrentFormat)
        {
            throw new FramePlanException("AUR-PLAN-001", "Unknown frame-plan format.");
        }
        FramePlanCompiler.ValidateDeclarations(plan.Graph);
        if (plan.Order.Distinct().Count() != plan.Order.Length
            || plan.Order.Any(id => id < 0 || id >= plan.Graph.Passes.Length)
            || !plan.Tape.Where(op => op.Op == FrameTapeOp.Pass).Select(op => op.Pass).SequenceEqual(plan.Order))
        {
            throw new FramePlanException("AUR-PLAN-001", "Tape and declared pass order disagree.");
        }
        var initialized = plan.Graph.Resources.Where(resource => resource.Initialized).Select(resource => resource.Id).ToHashSet();
        var dependencies = FramePlanCompiler.Dependencies(plan.Graph);
        var states = new Dictionary<int, FrameUse>();
        var barriers = new Dictionary<(int Pass, int Resource), FrameInstruction>();
        var executed = new HashSet<int>();
        foreach (FrameInstruction instruction in plan.Tape)
        {
            if (instruction.Pass < 0 || instruction.Pass >= plan.Graph.Passes.Length)
            {
                throw new FramePlanException("AUR-PLAN-001", "Invalid tape pass operand.");
            }
            if (instruction.Op == FrameTapeOp.Barrier)
            {
                if (instruction.Resource < 0 || instruction.Resource >= plan.Graph.Resources.Length
                    || executed.Contains(instruction.Pass)
                    || !plan.Graph.Passes[instruction.Pass].Uses.Any(use => use.Resource == instruction.Resource
                        && use.Access == instruction.After))
                {
                    throw new FramePlanException("AUR-PLAN-004", "Invalid or late synchronization instruction.");
                }
                if (!barriers.TryAdd((instruction.Pass, instruction.Resource), instruction))
                {
                    throw new FramePlanException("AUR-PLAN-004", "Duplicate synchronization instruction.");
                }
                continue;
            }
            if (instruction.Op != FrameTapeOp.Pass)
            {
                throw new FramePlanException("AUR-PLAN-001", "Unknown tape opcode.");
            }
            FramePass pass = plan.Graph.Passes[instruction.Pass];
            if (!dependencies[pass.Id].All(executed.Contains))
            {
                throw new FramePlanException("AUR-PLAN-003", "An explicit dependency has not executed: " + pass.Name);
            }
            foreach (FrameUse use in pass.Uses)
            {
                if ((!use.Writes || use.Access == FrameAccess.HistoryReadWrite) && !initialized.Contains(use.Resource))
                {
                    throw new FramePlanException("AUR-PLAN-002", pass.Name + " reads before initialization.");
                }
                if (states.TryGetValue(use.Resource, out var previous) && (previous.Writes || use.Writes))
                {
                    if (!barriers.TryGetValue((pass.Id, use.Resource), out var barrier)
                        || (barrier.Before & previous.Access) != previous.Access || barrier.After != use.Access)
                    {
                        throw new FramePlanException("AUR-PLAN-004", "Missing or insufficient barrier before " + pass.Name);
                    }
                }
                states[use.Resource] = previous.Resource == use.Resource && !previous.Writes && !use.Writes
                    ? new(use.Resource, previous.Access | use.Access) : use;
                if (use.Writes)
                {
                    initialized.Add(use.Resource);
                }
            }
            executed.Add(pass.Id);
        }
        foreach (int resource in plan.Graph.Outputs)
        {
            if (!initialized.Contains(resource))
            {
                throw new FramePlanException("AUR-PLAN-002", "A graph output was never initialized.");
            }
        }
        foreach (int pass in plan.Order)
        {
            foreach (FrameUse use in plan.Graph.Passes[pass].Uses.Where(use => use.Writes))
            {
                FrameUse? nextUse = plan.Order.SkipWhile(id => id != pass).Skip(1)
                    .SelectMany(id => plan.Graph.Passes[id].Uses).Where(next => next.Resource == use.Resource)
                    .Cast<FrameUse?>().FirstOrDefault();
                bool consumed = nextUse.HasValue && (!nextUse.Value.Writes || nextUse.Value.Access == FrameAccess.HistoryReadWrite);
                if (!consumed && !plan.Graph.Outputs.Contains(use.Resource) && !plan.Graph.Resources[use.Resource].AllowUnused)
                {
                    throw new FramePlanException("AUR-PLAN-006", "Unconsumed write: " + plan.Graph.Resources[use.Resource].Name);
                }
            }
        }
        VerifyBatches(plan);
        VerifyAliasing(plan);
        if (plan.Hash != plan.ComputeHash())
        {
            throw new FramePlanException("AUR-PLAN-007", "Frame-plan content hash mismatch.");
        }
    }

    private static void VerifyBatches(FramePlan plan)
    {
        if (!plan.Batches.Select(batch => batch.Id).SequenceEqual(Enumerable.Range(0, plan.Batches.Length))
            || !plan.Batches.SelectMany(batch => batch.Passes).Order().SequenceEqual(plan.Order.Order()))
        {
            throw new FramePlanException("AUR-PLAN-008", "Submission batches do not cover the executable passes.");
        }
        var batchForPass = plan.Batches.SelectMany(batch => batch.Passes.Select(pass => (pass, batch.Id))).ToDictionary(pair => pair.pass, pair => pair.Id);
        var dependencies = FramePlanCompiler.Dependencies(plan.Graph);
        foreach (FrameBatch batch in plan.Batches)
        {
            if (batch.Passes.Any(pass => plan.Graph.Passes[pass].Queue != batch.Queue)
                || !batch.Passes.SequenceEqual(plan.Order.Where(batch.Passes.Contains))
                || batch.WaitFor.Any(wait => wait < 0 || wait >= batch.Id))
            {
                throw new FramePlanException("AUR-PLAN-008", "Invalid queue or cyclic batch wait.");
            }
            foreach (int pass in batch.Passes)
            {
                foreach (int parent in dependencies[pass].Where(batchForPass.ContainsKey))
                {
                    int producer = batchForPass[parent];
                    if (producer > batch.Id)
                    {
                        throw new FramePlanException("AUR-PLAN-008", "A batch executes before its producer.");
                    }
                    if (plan.Batches[producer].Queue != batch.Queue && !batch.WaitFor.Contains(producer))
                    {
                        throw new FramePlanException("AUR-PLAN-008", "Cross-queue dependency lacks a timeline wait.");
                    }
                }
            }
        }
    }

    private static void VerifyAliasing(FramePlan plan)
    {
        int[] expected = plan.Graph.Resources.Where(resource => resource.Lifetime == FrameLifetime.Transient
            && plan.Order.Any(pass => plan.Graph.Passes[pass].Uses.Any(use => use.Resource == resource.Id)))
            .Select(resource => resource.Id).Order().ToArray();
        if (!plan.Allocations.Select(allocation => allocation.Resource).Order().SequenceEqual(expected)
            || plan.Allocations.Any(allocation => allocation.Slot < 0 || allocation.Slot >= plan.Graph.Resources.Length))
        {
            throw new FramePlanException("AUR-PLAN-009", "Transient placement does not cover the live resources.");
        }
        var dependencies = FramePlanCompiler.Dependencies(plan.Graph);
        foreach (FrameQueue queue in Enum.GetValues<FrameQueue>())
        {
            int[] ordered = plan.Order.Where(id => plan.Graph.Passes[id].Queue == queue).ToArray();
            for (int index = 1; index < ordered.Length; index++) dependencies[ordered[index]].Add(ordered[index - 1]);
        }
        foreach (var group in plan.Allocations.GroupBy(allocation => allocation.Slot))
        {
            FrameAllocation[] allocations = group.ToArray();
            var orderedOccupants = allocations.OrderBy(allocation => allocation.First).ToArray();
            for (int index = 1; index < orderedOccupants.Length; index++)
            {
                FrameAllocation previous = orderedOccupants[index - 1];
                FrameAllocation next = orderedOccupants[index];
                FrameAccess required = FrameAccess.None;
                foreach (int pass in plan.Order.Take(previous.Last + 1))
                {
                    foreach (FrameUse use in plan.Graph.Passes[pass].Uses.Where(use => use.Resource == previous.Resource))
                    {
                        required = use.Writes || new FrameUse(previous.Resource, required).Writes ? use.Access : required | use.Access;
                    }
                }
                if (!plan.Tape.Any(op => op.Op == FrameTapeOp.Barrier && op.Pass == plan.Order[next.First]
                    && op.Resource == next.Resource && (op.Before & required) == required))
                {
                    throw new FramePlanException("AUR-PLAN-009", "Alias ownership changes without synchronization.");
                }
            }
            foreach (FrameAllocation allocation in allocations)
            {
                if (allocation.Resource < 0 || allocation.Resource >= plan.Graph.Resources.Length
                    || plan.Graph.Resources[allocation.Resource].Lifetime != FrameLifetime.Transient)
                {
                    throw new FramePlanException("AUR-PLAN-009", "Only transient resources can alias.");
                }
                int[] uses = plan.Order.Select((pass, index) => (pass, index))
                    .Where(pair => plan.Graph.Passes[pair.pass].Uses.Any(use => use.Resource == allocation.Resource))
                    .Select(pair => pair.index).ToArray();
                if (allocation.First != uses[0] || allocation.Last != uses[^1])
                {
                    throw new FramePlanException("AUR-PLAN-009", "A published resource lifetime disagrees with the tape.");
                }
            }
            for (int left = 0; left < allocations.Length; left++)
            {
                for (int right = left + 1; right < allocations.Length; right++)
                {
                    var a = plan.Graph.Resources[allocations[left].Resource];
                    var b = plan.Graph.Resources[allocations[right].Resource];
                    if (a.Dedicated || b.Dedicated || !FramePlanCompiler.Compatible(a, b)
                        || !FramePlanCompiler.OrderedResources(plan.Graph, plan.Order, dependencies, a.Id, b.Id))
                    {
                        throw new FramePlanException("AUR-PLAN-009", "Transient resources alias while live: " + a.Name + "/" + b.Name);
                    }
                }
            }
        }
    }
}
