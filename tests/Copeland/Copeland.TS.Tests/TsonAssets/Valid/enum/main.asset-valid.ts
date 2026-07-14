const $schema: string = "copeland://fixtures/assets-enum";

enum Choice {
    None,
    Some(value: string),
}

function load(): Choice {
    const choice: Choice = tsonAsset("./choice.tson");
    return choice;
}
