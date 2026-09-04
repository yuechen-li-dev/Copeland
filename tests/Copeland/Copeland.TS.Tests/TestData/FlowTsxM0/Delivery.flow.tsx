enum DeliveryEvent {
    Start(amount: int),
    Retry(amount: int),
    Accept(amount: int),
    Reject(code: int),
    Reset,
    Cancel,
    Tick(amount: int),
}

function nextSequence(value: int): int {
    return value + 1;
}

export default (
    <Flow
        name="Delivery"
        events={DeliveryEvent}
        result="int"
        failure="string"
        board={{
            attempts: 0,
            total: 0,
            sequence: 0,
            accepted: false
        }}
    >

        <State name="Idle" initial>
            {Start(amount) when amount > 0 => Staging {
                board.total = amount;
                board.attempts = board.attempts + 1;
                board.sequence = nextSequence(board.sequence);
            }}
            {Cancel => Cancelled}
        </State>

        <State name="Staging">
            {Tick(amount) when amount > 0 => Staging {
                board.total = board.total + amount;
                board.sequence = nextSequence(board.sequence);
            }}
            {Retry(amount) when amount > 0 => Retrying {
                board.attempts = board.attempts + 1;
                board.total = board.total + amount;
            }}
            {Accept(amount) when amount >= 0 => Accepted {
                board.total = board.total + amount;
                board.accepted = true;
            }}
            {Reject(code) => Rejected}
        </State>

        <State name="Retrying">
            {Tick(amount) when amount > 0 => Retrying {
                board.total = board.total + amount;
            }}
            {Accept(amount) when amount >= 0 => Accepted {
                board.total = board.total + amount;
                board.accepted = true;
            }}
            {Reject(code) => Rejected}
            {Cancel => Cancelled}
        </State>

        <State name="Accepted">
            {Tick(amount) when amount > 0 => Completing {
                board.total = board.total + amount;
            }}
            {Reset => Idle {
                board.total = 0;
                board.attempts = 0;
                board.accepted = false;
            }}
        </State>

        <State name="Completing">
            {Accept(amount) => Completed {
                board.total = board.total + amount;
            }}
            {Reject(code) => Rejected}
        </State>

        <State name="Completed">
            <Finish value={board.total} />
        </State>

        <State name="Rejected">
            <Fail error="delivery rejected" />
        </State>

        <State name="Cancelled">
            <Fail error="delivery cancelled" />
        </State>
    </Flow>
);
