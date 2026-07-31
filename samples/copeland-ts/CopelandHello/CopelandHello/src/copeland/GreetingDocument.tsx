import { Greeting, GreetingStyle } from "./Program";
import { normalizeName } from "./Greeting";

export function greetingDocument(name: string): Document {
    const style: GreetingStyle = GreetingStyle.Friendly;
    const message: string = match style {
        Friendly => "Hello",
        Formal => "Greetings",
    };
    const original: Greeting = { recipient: normalizeName(name), message };
    const updated: Greeting = original with { recipient: "Copeland" };

    return <Document>
        <Heading level="1">{updated.message}, {updated.recipient}</Heading>
        <Paragraph>TypeScript computes. TS-XML describes.</Paragraph>
    </Document>;
}
