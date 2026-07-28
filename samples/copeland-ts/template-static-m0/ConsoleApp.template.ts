// CTS-TEMPLATE-STATIC-M0 dogfood source. The artifact constructors are the only
// permitted output API; this source never reads or writes the host filesystem.
record ConsoleAppConfig {
    name: string;
    includeTests: boolean;
}

import { BaseProject } from "./BaseProject.template.ts";

template ProgramSource(): ProjectTree {
    static if (true) {
        emit(sourceFile("Program.cs", `Console.WriteLine("Hello from Copeland template");
`));
    }
}

template ConsoleApp<TConfig extends ConsoleAppConfig>(): ProjectTree {
    emit(BaseProject());
    static for (const source of ["Program"]) {
        emit(ProgramSource());
    }
    static match "Console" {
        Console => { }
    }
}
