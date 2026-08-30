namespace Machina.Runtime.Input;

public static class UiInputEventExtensions
{
    public static bool TryGetPointerPosition(this UiInputEvent inputEvent, out PointerPoint position)
    {
        switch (inputEvent)
        {
            case UiPointerMoved moved:
                position = moved.Position;
                return true;
            case UiPointerButtonChanged button:
                position = button.Position;
                return true;
            case UiPointerWheel wheel:
                position = wheel.Position;
                return true;
            default:
                position = default;
                return false;
        }
    }

    public static bool IsPrimaryPressed(this UiInputEvent inputEvent)
    {
        return inputEvent is UiPointerButtonChanged
        {
            Button: UiPointerButton.Primary,
            IsPressed: true,
        };
    }

    public static bool IsPointerReleased(this UiInputEvent inputEvent)
    {
        return inputEvent is UiPointerButtonChanged { IsPressed: false };
    }

    public static bool IsPointerMoved(this UiInputEvent inputEvent)
    {
        return inputEvent is UiPointerMoved;
    }

    public static bool IsWheel(this UiInputEvent inputEvent, out double deltaY)
    {
        if (inputEvent is UiPointerWheel wheel)
        {
            deltaY = wheel.DeltaY;
            return true;
        }

        deltaY = 0;
        return false;
    }

    public static UiInputEvent WithPointerPosition(this UiInputEvent inputEvent, PointerPoint position)
    {
        return inputEvent switch
        {
            UiPointerMoved moved => moved with { Position = position },
            UiPointerButtonChanged button => button with { Position = position },
            UiPointerWheel wheel => wheel with { Position = position },
            _ => inputEvent,
        };
    }
}
