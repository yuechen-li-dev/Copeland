"""Write deterministic teacher-forced E2B prefill and decode token IDs."""

import argparse
import json
from pathlib import Path

from tokenizers import Tokenizer


def write_sequence(output, prompt, continuation):
    output.write_text(" ".join(map(str, prompt)) + "\n" + "\n".join(map(str, continuation)) + "\n")
    print(f"{output.name}: {len(prompt)} prompt, {len(continuation)} decode")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("tokenizer", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)
    data = json.loads(args.tokenizer.read_text(encoding="utf8"))
    # Preserve BPE pairs on tokenizers releases that expect space-separated merges.
    try:
        tokenizer = Tokenizer.from_str(json.dumps(data))
    except Exception:
        data["model"]["merges"] = [" ".join(pair) for pair in data["model"]["merges"]]
        tokenizer = Tokenizer.from_str(json.dumps(data))

    message = (
        "Write a practical checklist for editing a short documentary. Start with how to inspect the source footage, "
        "organize interview clips, and pick the strongest material for the opening. Then explain how to balance dialogue "
        "against music and room tone. Finish with advice about captions, delivery formats and checking an export on a "
        "different screen. Include the checks the editor should make immediately before the first public screening."
    )
    reply = (
        "First, review the footage and tag complete thoughts and usable reaction shots. Build a clear opening around one "
        "interview excerpt, and use the other clips to establish context. Listen through the whole cut on headphones, then "
        "check caption spelling, export a screening copy and watch it from start to finish."
    )
    chat = f"<bos><|turn>user\n{message}<turn|>\n<|turn>model\n"
    prompt = tokenizer.encode(chat, add_special_tokens=False).ids
    continuation = tokenizer.encode(reply, add_special_tokens=False).ids
    text = prompt + continuation
    write_sequence(args.output / "ids16.txt", range(100, 116), range(500, 508))
    write_sequence(args.output / "ids64.txt", range(100, 164), range(500, 532))
    write_sequence(args.output / "ids-text.txt", prompt, continuation[:32])
    write_sequence(args.output / "ids-text64.txt", text[:64], text[64:96])
    repeated = text * 9
    write_sequence(args.output / "ids-text512.txt", repeated[:512], repeated[512:528])


if __name__ == "__main__":
    main()
