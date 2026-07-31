// CTS-TYPE-TEMPLATE-M1 dogfood source. The artifact constructors are the only
// permitted output API; this source never reads or writes the host filesystem.
type ConsoleConfig = {
    name: string;
    includeTests: boolean;
};

import { BaseProject } from "./BaseProject.template.ts";

template<static config: ConsoleConfig> ProgramSource: ProjectTree {
    static if (true) {
        emit(sourceFile("Program.cs", `Console.WriteLine("Hello from ${config.name}");
`));
    }
}

template<static config: ConsoleConfig> ConsoleApp: ProjectTree {
    emit(instantiate BaseProject<>);
    static for (const source of ["Program"]) {
        emit(instantiate ProgramSource<config: config>);
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
template<> ConsoleDogfood: ProjectTree {
    emit(instantiate ConsoleApp<config: { name: "Copeland template", includeTests: true }>);
}
