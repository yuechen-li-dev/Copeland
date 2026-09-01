interface Positioned {
    x: number;
    y: number;
}

interface Weighted {
    weight: number;
}

record Position {
    x: number;
    y: number;
}

record LocalPoint {
    x: int;
    y: int;
}

record OrderLine {
    sku: string;
    quantity: int;
    unitPrice: number;
}

class Parcel {
    public code: string;
    private normalizedCode: string;
    weight: number;

    constructor(code: string, weight: number): Parcel ! string {
        if (weight < 0) {
            return err("weight must be nonnegative");
        }
        return ok({
            code,
            normalizedCode: Parcel.normalize(code),
            weight
        });
    }

    private normalize(code: string): string {
        return code;
    }

    adjust(parcel: Parcel, delta: number): Parcel {
        return parcel with { weight: parcel.weight + delta };
    }

    identity<T>(value: T): T {
        return value;
    }
}

record Customer {
    name: string;
    region: string;
    position: Position;
}

enum Fulfillment {
    Pending,
    Packed(attempts: int),
    Shipped(tracking: string, position: Position),
    Rejected(reason: string),
}

record Order {
    id: int;
    customer: Customer;
    lines: OrderLine[];
    parcel: Parcel;
    fulfillment: Fulfillment;
}

function identity<T>(value: T): T {
    return value;
}

function coordinateTotal<T extends Positioned>(value: T): number {
    return value.x + value.y;
}

function publicWeight<T extends Weighted>(value: T): number {
    return value.weight;
}

function lineTotal(line: OrderLine): number {
    return Float.From(line.quantity) * line.unitPrice;
}

function orderTotal(order: Order): number {
    let total: number = 0;
    for (const line of order.lines) {
        total = total + lineTotal(line);
    }
    return total;
}

function validateQuantity(quantity: int): int ! string {
    if (quantity < 1) {
        return err("quantity must be positive");
    }
    return ok(quantity);
}

function reserve(line: OrderLine): OrderLine ! string {
    const quantity: int = validateQuantity(line.quantity)?;
    return ok(line with { quantity: quantity });
}

function describe(status: Fulfillment): string {
    return match status {
        Pending => "pending",
        Packed(attempts) => "packed",
        Shipped(tracking, position) => tracking,
        Rejected(reason) => reason,
    };
}

function moveLocalItem(): int {
    const item = {
        name: "sample",
        position: {
            x: 1,
            y: 2
        }
    };
    const peer = identity({
        name: "peer",
        position: {
            x: 3,
            y: 4
        }
    });
    const moved = item with {
        position: item.position with {
            x: peer.position.x
        }
    };
    return moved.position.x + moved.position.y;
}

function orderedShapePressure(): int {
    const a = { x: 1, y: 2 };
    const b = { x: 3, y: 4 };
    const c = { y: 4, x: 3 };
    return a.x + b.y + c.x;
}

function makeLocalResult(valid: boolean): LocalPoint ! string {
    if (valid) {
        return ok({ x: 20, y: 22 });
    }
    return err("missing");
}

function readLocalResult(): int {
    return match makeLocalResult(true) {
        ok(value) => value.x + value.y,
        err(error) => 0,
    };
}

function evaluationTrace(buffer: MutableArray<int>, value: int): int {
    buffer[0] = buffer[0] * 10 + value;
    return value;
}

function evaluationOrder(): int {
    const buffer: MutableArray<int> = MutableArray<int>(1);
    const value = {
        first: evaluationTrace(buffer, 1),
        second: evaluationTrace(buffer, 2)
    };
    const moved = value with {
        first: evaluationTrace(buffer, 3),
        second: evaluationTrace(buffer, 4)
    };
    return buffer[0] + moved.first + moved.second;
}

function main(): number {
    const customer: Customer = {
        name: "Ada",
        region: "west",
        position: { x: 5, y: 8 }
    };
    const first: OrderLine = { sku: "A", quantity: 2, unitPrice: 10.5 };
    const second: OrderLine = { sku: "B", quantity: 1, unitPrice: 4 };
    const parcel: Parcel = Parcel("PKG-7", 10)!;
    const adjusted: Parcel = Parcel.adjust(parcel, 2);
    const order: Order = {
        id: 7,
        customer: customer,
        lines: [first, second],
        parcel: adjusted,
        fulfillment: Fulfillment.Packed(1)
    };
    const reserved: OrderLine = reserve(first)!;
    const status: string = describe(order.fulfillment);
    const namedBoundary: number = coordinateTotal<Position>({ x: 5, y: 8 });
    return orderTotal(order)
        + Float.From(reserved.quantity)
        + namedBoundary
        + Float.From(moveLocalItem())
        + Float.From(orderedShapePressure())
        + Float.From(readLocalResult())
        + Float.From(evaluationOrder())
        + Float.From(status.length)
        + Parcel.identity<number>(publicWeight(adjusted));
}
