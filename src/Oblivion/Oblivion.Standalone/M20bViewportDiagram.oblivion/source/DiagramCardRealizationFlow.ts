// Exact M20a semantic source copied for the isolated M20b viewport/backend comparison.
flow DiagramCardRealizationFlow -> number {
    board { outcome: number = 0; }

    event SourceReady(present: boolean); event SourceMissing(present: boolean);
    event Parsed(succeeded: boolean); event ParseFailed(succeeded: boolean);
    event Bound(succeeded: boolean); event BindFailed(succeeded: boolean);
    event Lowered(succeeded: boolean); event LowerFailed(succeeded: boolean);
    event RendererAvailable(available: boolean); event RendererUnavailable(available: boolean);
    event Emitted(succeeded: boolean); event EmitFailed(succeeded: boolean);
    event ArtifactValid(valid: boolean); event ArtifactCorrupt(valid: boolean);
    event CacheHit(valid: boolean); event CacheStale(valid: boolean);
    event Projected(valid: boolean); event ProjectionFailed(valid: boolean);
    event Mounted(valid: boolean); event HostFailed(valid: boolean);
    event Approved(approved: boolean); event RevisionRequested(approved: boolean);
    event Repair(); event Retry(); event Cancel();

    state WorkspaceIntake initial { on SourceReady(present) when present == true -> Parsing; on SourceMissing(present) when present == false -> Diagnostics; }
    state Parsing { on Parsed(succeeded) when succeeded == true -> Binding; on ParseFailed(succeeded) when succeeded == false -> Diagnostics; }
    state Binding { on Bound(succeeded) when succeeded == true -> Lowering; on BindFailed(succeeded) when succeeded == false -> Diagnostics; }
    state Lowering { on Lowered(succeeded) when succeeded == true -> BackendSelection; on LowerFailed(succeeded) when succeeded == false -> Diagnostics; }
    state BackendSelection { on RendererAvailable(available) when available == true -> Emitting; on RendererUnavailable(available) when available == false -> RendererRecovery; }
    state Emitting { on Emitted(succeeded) when succeeded == true -> ArtifactValidation; on EmitFailed(succeeded) when succeeded == false -> RendererRecovery; }
    state ArtifactValidation { on ArtifactValid(valid) when valid == true -> CacheQualification; on ArtifactCorrupt(valid) when valid == false -> RendererRecovery; }
    state CacheQualification { on CacheHit(valid) when valid == true -> CardProjection; on CacheStale(valid) when valid == false -> Emitting; }
    state CardProjection { on Projected(valid) when valid == true -> CardRealization; on ProjectionFailed(valid) when valid == false -> Diagnostics; }
    state CardRealization { on Mounted(valid) when valid == true -> HumanReview; on HostFailed(valid) when valid == false -> Diagnostics; }
    state HumanReview { on Approved(approved) when approved == true -> Accepted { board.outcome = 1; }; on RevisionRequested(approved) when approved == false -> SourceRepair; on Retry() -> RendererRecovery; }
    state Diagnostics { on Repair() -> SourceRepair; on Retry() -> CardProjection; on Cancel() -> Rejected { board.outcome = -1; }; }
    state SourceRepair { on Retry() -> Parsing; on Cancel() -> Rejected { board.outcome = -1; }; }
    state RendererRecovery { on RendererAvailable(available) when available == true -> Emitting; on RendererUnavailable(available) when available == false -> Diagnostics; on Cancel() -> Rejected { board.outcome = -1; }; }
    state Accepted { finish board.outcome; }
    state Rejected { finish board.outcome; }
}
