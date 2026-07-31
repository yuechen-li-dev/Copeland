using System.Collections;

namespace CopelandHello;

public static class TextDocumentAssertions
{
    public static bool HasExpectedGreetingStructure(object value)
    {
        object root = value.GetType().GetProperty("Root")!.GetValue(value)!;
        if (!Equals(root.GetType().GetProperty("Kind")!.GetValue(root), "Document"))
        {
            return false;
        }

        object[] documentChildren = ((IEnumerable)root.GetType().GetProperty("Children")!.GetValue(root)!).Cast<object>().ToArray();
        if (documentChildren.Length != 2)
        {
            return false;
        }

        object heading = documentChildren[0].GetType().GetProperty("Node")!.GetValue(documentChildren[0])!;
        object paragraph = documentChildren[1].GetType().GetProperty("Node")!.GetValue(documentChildren[1])!;
        object[] headingChildren = ((IEnumerable)heading.GetType().GetProperty("Children")!.GetValue(heading)!).Cast<object>().ToArray();
        object[] paragraphChildren = ((IEnumerable)paragraph.GetType().GetProperty("Children")!.GetValue(paragraph)!).Cast<object>().ToArray();

        return Equals(heading.GetType().GetProperty("Kind")!.GetValue(heading), "Heading")
            && Equals(paragraph.GetType().GetProperty("Kind")!.GetValue(paragraph), "Paragraph")
            && headingChildren.Select(child => child.GetType().Name).SequenceEqual(["EmbeddedTextValue", "TextRun", "EmbeddedTextValue"])
            && Equals(headingChildren[0].GetType().GetProperty("Value")!.GetValue(headingChildren[0]), "Hello")
            && Equals(headingChildren[2].GetType().GetProperty("Value")!.GetValue(headingChildren[2]), "Copeland")
            && paragraphChildren.Select(child => child.GetType().Name).SequenceEqual(["TextRun"])
            && Equals(paragraphChildren[0].GetType().GetProperty("Value")!.GetValue(paragraphChildren[0]), "TypeScript computes. TS-XML describes.");
    }
}
