interface Named {
    name: string;
}

record Service {
    name: string;
    port: int;
    secure: boolean;
}

record Worker {
    name: string;
    queue: string;
    retries: int;
}

type PublicService = {
    name: string;
    port: int;
};

enum Mode {
    Development,
    Production(region: string),
    Maintenance(reason: string, until: string),
}

function ParseConfig(): void {
    return;
}

function ValidateConfig(): void {
    ParseConfig();
}

function CompileService(): void {
    ParseConfig();
    ValidateConfig();
    ValidateConfig();
}

template<type T extends Named = Service> TypeInventory: ProjectTree {
    static for (const field of reflect fieldsOf<T>()) {
        emit(textFile(
            `${reflect nameOf<T>()}-${field.name}.txt`,
            `${field.typeName}:${field.optional}:${field.readonly}`
        ));
    }
}

template<type T = Mode> EnumInventory: ProjectTree {
    static for (const item of reflect enumCasesOf<T>()) {
        emit(textFile(
            `mode-${item.name}.txt`,
            `${item.name}:${item.payloadCount}`
        ));
    }
}

template<> CallInventory: ProjectTree {
    const calls = reflect callsOf<CompileService>();
    static for (const call of calls) {
        emit(textFile(
            `call-${call.source.startLine}-${call.source.startColumn}.txt`,
            `${call.kind}:${call.source.startLine}`
        ));
    }
}

template<static value: string> Label: string {
    return value;
}

template<static label: string, static includeWorker: boolean> LabeledInventory: ProjectTree {
    static if (includeWorker) {
        emit(instantiate TypeInventory<Worker>);
    } else {
        emit(textFile("label.txt", label));
    }
}

template<> BurnInMetadata: ProjectTree {
    emit(instantiate TypeInventory<Service>);
    emit(instantiate LabeledInventory<label: "worker", includeWorker: true>);
    emit(instantiate EnumInventory<Mode>);
    emit(instantiate CallInventory<>);
    emit(textFile("public-service.txt", reflect nameOf<PublicService>()));
    emit(textFile("memo-a.txt", instantiate Label<value: "same">));
    emit(textFile("memo-b.txt", instantiate Label<value: "same">));
}
