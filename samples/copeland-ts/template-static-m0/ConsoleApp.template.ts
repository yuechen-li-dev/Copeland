// CTS-TYPE-TEMPLATE-M1 dogfood source. The artifact constructors are the only
// permitted output API; this source never reads or writes the host filesystem.
type ConsoleConfig = {
    name: string;
    includeTests: boolean;
};

import { BaseProject } from "./BaseProject.template.ts";

template ProgramSource(static config: ConsoleConfig): ProjectTree {
    static if (true) {
        emit(sourceFile("Program.cs", `Console.WriteLine("Hello from ${config.name}");
`));
    }
}

template ConsoleApp(static config: ConsoleConfig): ProjectTree {
    emit(BaseProject());
    static for (const source of ["Program"]) {
        emit(ProgramSource(config));
    }
    static if (config.includeTests) {
        emit(sourceFile("ConsoleApp.Tests.cs", `// Tests for ${config.name}
`));
    }
    static match "Console" {
        Console => { }
    }
}

// Entry point retained for CLI preview/materialization dogfood. The generated
// application itself is produced by the typed static-value ConsoleApp call.
template ConsoleDogfood(): ProjectTree {
    emit(ConsoleApp({ name: "Copeland template", includeTests: true }));
}
