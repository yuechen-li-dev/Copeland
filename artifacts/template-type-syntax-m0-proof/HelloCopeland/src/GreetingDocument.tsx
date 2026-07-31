import { Greeting, GreetingStyle } from "./Program";

export function greetingDocument(name: string): Document {
    const style: GreetingStyle = GreetingStyle.Friendly;
    const message: string = match style {
        Friendly => "Hello",
        Formal => "Greetings",
    };
    const initial: Greeting = { recipient: name, message };
    const updated: Greeting = initial with { recipient: "Copeland" };
    return <Document><Paragraph>{updated.message}, {updated.recipient}</Paragraph></Document>;
}
