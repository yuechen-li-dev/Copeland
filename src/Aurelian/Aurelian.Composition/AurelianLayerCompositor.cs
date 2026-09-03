namespace Aurelian.Composition;

public sealed class AurelianLayerCompositor : IDisposable
{
    private readonly List<LayerEntry> layers = [];
    private readonly Dictionary<LayerId, LayerEntry> layersById = [];
    private LayerSurfaceDescriptor surface;
    private long nextSequence;
    private bool attached;
    private LayerId? focusOwner;
    private LayerId? captureOwner;

    public AurelianLayerCompositor(LayerSurfaceDescriptor surface)
    {
        this.surface = surface ?? throw new ArgumentNullException(nameof(surface));
    }

    public LayerSurfaceDescriptor Surface => surface;
    public LayerId? FocusOwner => focusOwner;
    public LayerId? CaptureOwner => captureOwner;

    public void Add(IAurelianLayer layer)
    {
        ArgumentNullException.ThrowIfNull(layer);
        LayerDescriptor descriptor = ValidateDescriptor(layer.Describe());
        if (layersById.ContainsKey(descriptor.Id))
        {
            throw new ArgumentException($"Layer '{descriptor.Id}' is already registered.", nameof(layer));
        }

        var entry = new LayerEntry(layer, descriptor, nextSequence++);
        layers.Add(entry);
        layersById.Add(descriptor.Id, entry);
        if (attached)
        {
            layer.Attach(surface);
        }
    }

    public void Attach()
    {
        if (attached)
        {
            return;
        }

        foreach (LayerEntry entry in OrderedLayers())
        {
            entry.Layer.Attach(surface);
        }
        attached = true;
    }

    public void Resize(LayerSurfaceDescriptor resizedSurface)
    {
        surface = resizedSurface ?? throw new ArgumentNullException(nameof(resizedSurface));
        foreach (LayerEntry entry in OrderedLayers())
        {
            entry.Layer.Resize(surface);
            entry.Descriptor = ValidateDescriptor(entry.Layer.Describe());
        }
    }

    public void SetEnabled(LayerId layerId, bool enabled)
    {
        LayerEntry entry = GetEntry(layerId);
        entry.EnabledOverride = enabled;
        entry.Descriptor = entry.Descriptor with { Enabled = enabled };
        if (!enabled)
        {
            if (focusOwner == layerId)
            {
                focusOwner = null;
            }
            if (captureOwner == layerId)
            {
                captureOwner = null;
            }
        }
    }

    public void SetZOrder(LayerId layerId, int zOrder)
    {
        LayerEntry entry = GetEntry(layerId);
        entry.ZOrderOverride = zOrder;
        entry.Descriptor = entry.Descriptor with { ZOrder = zOrder };
    }

    public IReadOnlyList<LayerPresentationDto> RunFrame(ulong frameId, TimeSpan elapsed)
    {
        EnsureAttached();
        RefreshDescriptors();
        LayerEntry[] ordered = OrderedLayers().Where(static entry => entry.Descriptor.Enabled).ToArray();
        var updateContext = new LayerUpdateContext(frameId, elapsed);
        foreach (LayerEntry entry in ordered)
        {
            entry.Layer.Update(updateContext);
        }

        var presentations = new List<LayerPresentationDto>(ordered.Length);
        var presentationContext = new LayerPresentationContext(frameId, surface);
        foreach (LayerEntry entry in ordered)
        {
            try
            {
                LayerPresentationDto presentation = entry.Layer.Present(presentationContext);
                if (presentation.Layer != entry.Descriptor.Id)
                {
                    throw new InvalidOperationException($"Layer '{entry.Descriptor.Id}' presented identity '{presentation.Layer}'.");
                }
                presentations.Add(presentation);
            }
            catch (Exception exception)
            {
                throw new AurelianLayerPresentationException(entry.Descriptor.Id, frameId, exception);
            }
        }
        return presentations;
    }

    public LayerInputRoutingResult RouteInput(LayerInputEvent input)
    {
        ArgumentNullException.ThrowIfNull(input);
        EnsureAttached();
        RefreshDescriptors();
        var visited = new List<LayerId>();

        if (captureOwner is LayerId captured && TryRoute(captured, input, visited, out LayerInputRoutingResult? captureResult))
        {
            return captureResult!;
        }

        if (input is LayerKeyChanged or LayerTextEntered
            && focusOwner is LayerId focused
            && !visited.Contains(focused)
            && TryRoute(focused, input, visited, out LayerInputRoutingResult? focusResult))
        {
            return focusResult!;
        }

        foreach (LayerEntry entry in OrderedLayers().Reverse())
        {
            if (visited.Contains(entry.Descriptor.Id) || !IsEligible(entry, input))
            {
                continue;
            }
            if (TryRoute(entry.Descriptor.Id, input, visited, out LayerInputRoutingResult? result))
            {
                return result!;
            }
        }

        return new LayerInputRoutingResult(false, null, focusOwner, captureOwner, visited);
    }

    public void SendToLayer<TPayload>(LayerMessage<TPayload> message)
    {
        if (message.Target is not LayerId target)
        {
            throw new ArgumentException("Application-to-layer messages require a target.", nameof(message));
        }
        LayerEntry entry = GetEntry(target);
        if (entry.Layer is not IAurelianLayerMessageReceiver<TPayload> receiver)
        {
            throw new InvalidOperationException($"Layer '{target}' does not receive payload '{typeof(TPayload).FullName}'.");
        }
        receiver.Receive(message);
    }

    public void Dispose()
    {
        foreach (LayerEntry entry in OrderedLayers().Reverse())
        {
            entry.Layer.Detach();
            if (entry.Layer is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
        attached = false;
    }

    private bool TryRoute(
        LayerId layerId,
        LayerInputEvent input,
        List<LayerId> visited,
        out LayerInputRoutingResult? routingResult)
    {
        LayerEntry entry = GetEntry(layerId);
        if (!IsEligible(entry, input))
        {
            routingResult = null;
            return false;
        }

        visited.Add(layerId);
        LayerInputResult result = entry.Layer.HandleInput(ToLayerCoordinates(input, entry.Descriptor, surface.Scale));
        if (result.RequestFocus)
        {
            focusOwner = layerId;
        }
        if (result.RequestCapture)
        {
            captureOwner = layerId;
        }
        if (result.ReleaseCapture && captureOwner == layerId)
        {
            captureOwner = null;
        }

        bool consumed = result.Consumed || entry.Descriptor.InputPolicy == LayerInputPolicy.Opaque;
        routingResult = consumed
            ? new LayerInputRoutingResult(true, layerId, focusOwner, captureOwner, visited.ToArray())
            : null;
        return consumed;
    }

    private static bool IsEligible(LayerEntry entry, LayerInputEvent input)
    {
        if (!entry.Descriptor.Enabled || entry.Descriptor.InputPolicy == LayerInputPolicy.None)
        {
            return false;
        }
        return input switch
        {
            LayerPointerMoved pointer => entry.Descriptor.Viewport.Contains(pointer.Position.X, pointer.Position.Y),
            LayerPointerButtonChanged pointer => entry.Descriptor.Viewport.Contains(pointer.Position.X, pointer.Position.Y),
            LayerPointerWheel pointer => entry.Descriptor.Viewport.Contains(pointer.Position.X, pointer.Position.Y),
            _ => true
        };
    }

    private static LayerInputEvent ToLayerCoordinates(LayerInputEvent input, LayerDescriptor descriptor, double scale)
    {
        return input switch
        {
            LayerPointerMoved moved => new LayerPointerMoved(
                descriptor.Viewport.ToLocal(moved.Position, scale),
                moved.PreviousPosition is LayerPoint previous
                    ? descriptor.Viewport.ToLocal(previous, scale)
                    : null),
            LayerPointerButtonChanged changed => changed with
            {
                Position = descriptor.Viewport.ToLocal(changed.Position, scale)
            },
            LayerPointerWheel wheel => wheel with
            {
                Position = descriptor.Viewport.ToLocal(wheel.Position, scale)
            },
            _ => input
        };
    }

    private LayerEntry[] OrderedLayers()
    {
        return layers.OrderBy(static entry => entry.Descriptor.ZOrder)
            .ThenBy(static entry => entry.Descriptor.Id.Value, StringComparer.Ordinal)
            .ThenBy(static entry => entry.Sequence)
            .ToArray();
    }

    private void RefreshDescriptors()
    {
        foreach (LayerEntry entry in layers)
        {
            LayerDescriptor refreshed = ValidateDescriptor(entry.Layer.Describe());
            if (refreshed.Id != entry.Descriptor.Id)
            {
                throw new InvalidOperationException(
                    $"Layer '{entry.Descriptor.Id}' changed its stable identity to '{refreshed.Id}'.");
            }
            entry.Descriptor = refreshed with
            {
                Enabled = entry.EnabledOverride ?? refreshed.Enabled,
                ZOrder = entry.ZOrderOverride ?? refreshed.ZOrder
            };
        }
    }

    private LayerEntry GetEntry(LayerId layerId)
    {
        return layersById.TryGetValue(layerId, out LayerEntry? entry)
            ? entry
            : throw new KeyNotFoundException($"Layer '{layerId}' is not registered.");
    }

    private static LayerDescriptor ValidateDescriptor(LayerDescriptor descriptor)
    {
        if (string.IsNullOrWhiteSpace(descriptor.Id.Value))
        {
            throw new ArgumentException("Layer descriptor requires a stable identity.", nameof(descriptor));
        }
        return descriptor;
    }

    private void EnsureAttached()
    {
        if (!attached)
        {
            throw new InvalidOperationException("The compositor must be attached before frames or input are routed.");
        }
    }

    private sealed class LayerEntry(IAurelianLayer layer, LayerDescriptor descriptor, long sequence)
    {
        public IAurelianLayer Layer { get; } = layer;
        public LayerDescriptor Descriptor { get; set; } = descriptor;
        public long Sequence { get; } = sequence;
        public bool? EnabledOverride { get; set; }
        public int? ZOrderOverride { get; set; }
    }
}

public sealed class AurelianLayerPresentationException : Exception
{
    public AurelianLayerPresentationException(LayerId layer, ulong frameId, Exception innerException)
        : base($"Layer '{layer}' failed while presenting frame {frameId}: {innerException.Message}", innerException)
    {
        Layer = layer;
        FrameId = frameId;
    }

    public LayerId Layer { get; }
    public ulong FrameId { get; }
}
