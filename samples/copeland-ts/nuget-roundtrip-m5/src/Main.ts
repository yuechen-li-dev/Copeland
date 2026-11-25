import { Address, DeserializeShipment, NugetError, SerializeShipment, Shipment } from "./generated/Nuget";

function SameAddress(actual: Address, expected: Address): boolean {
    return actual.city == expected.city
        && (actual.unit ?? "(none)") == (expected.unit ?? "(none)")
        && (actual.floor ?? -1) == (expected.floor ?? -1);
}

function SameBackup(actual: Option<Address>, expected: Address): boolean {
    return match actual {
        Some(value) => SameAddress(value, expected),
        None => false,
    };
}

function Preserved(actual: Shipment, original: Shipment, backup: Address): boolean {
    if (actual.name != original.name || !SameAddress(actual.address, original.address)
        || !SameBackup(actual.backup, backup) || (actual.active ?? true)) {
        return false;
    }
    if (actual.stops.length != 2 || actual.tags.length != 2) {
        return false;
    }
    return SameAddress(actual.stops[0], original.stops[0])
        && SameAddress(actual.stops[1], original.stops[1])
        && (actual.tags[0] ?? "(none)") == "(none)"
        && (actual.tags[1] ?? "(none)") == "";
}

function CheckAbsent(result: Option<Shipment> ! NugetError): boolean {
    const missingFloor: number = -1;
    return match result {
        ok(shipment) => match shipment {
            Some(value) => (value.address.unit ?? "missing") == "missing"
                && (value.address.floor ?? missingFloor) == missingFloor
                && (value.active ?? true),
            None => false,
        },
        err(error) => false,
    };
}

function MatchesRoundTrip(found: Option<Shipment>, shipment: Shipment, backup: Address): boolean {
    return match found {
        Some(value) => Preserved(value, shipment, backup),
        None => false,
    };
}

function IsNullRoot(found: Option<Shipment>): boolean {
    return match found {
        None => true,
        Some(value) => false,
    };
}

export async function Main(): string {
    const address: Address = { city: "Montréal", unit: None, floor: Some(0) };
    const backup: Address = { city: "Québec", unit: Some("4B"), floor: None };
    const shipment: Shipment = {
        name: 'Ship "the docs" 🌍',
        address: address,
        backup: Some(backup),
        active: Some(false),
        stops: [backup, address],
        tags: [None, Some("")],
    };
    const pendingWrite = SerializeShipment(shipment);
    const written: string ! NugetError = await pendingWrite;
    const json: string = match written {
        ok(value) => value,
        err(error) => "serialize failed: " + error.message,
    };

    const pendingRoundTrip = DeserializeShipment(json);
    const pendingNull = DeserializeShipment("null");
    const pendingAbsent = DeserializeShipment('{"name":"optional","address":{"city":"Lyon"},"stops":[],"tags":[]}');
    const pendingInvalid = DeserializeShipment('{"definitelyBroken"');
    const pendingRequired = DeserializeShipment('{"name":"missing address","stops":[],"tags":[]}');
    const roundTrip: Option<Shipment> ! NugetError = await pendingRoundTrip;
    const nullRoot: Option<Shipment> ! NugetError = await pendingNull;
    const absent: Option<Shipment> ! NugetError = await pendingAbsent;
    const invalid: Option<Shipment> ! NugetError = await pendingInvalid;
    const required: Option<Shipment> ! NugetError = await pendingRequired;

    const matches: boolean = match roundTrip {
        ok(found) => MatchesRoundTrip(found, shipment, backup),
        err(error) => false,
    };
    const sawNull: boolean = match nullRoot {
        ok(found) => IsNullRoot(found),
        err(error) => false,
    };
    const sawInvalid: boolean = match invalid {
        ok(value) => false,
        err(error) => error.message.length > 0,
    };
    const sawRequired: boolean = match required {
        ok(value) => false,
        err(error) => error.message.length > 0,
    };
    if (matches && sawNull && CheckAbsent(absent) && sawInvalid && sawRequired) {
        return json;
    }
    return "round trip failed: " + json;
}
