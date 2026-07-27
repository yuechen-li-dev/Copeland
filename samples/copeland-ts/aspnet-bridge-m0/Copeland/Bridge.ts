using System.Text.Json;

export record SerializeRequest {
    message: string;
    count: int;
}

export record BridgeError {
    kind: string;
    message: string;
}

export remote function SerializeState(
    request: SerializeRequest
): string ! BridgeError {
    return JsonSerializer.Serialize(request);
}
