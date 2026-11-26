using static Aurelian.Inference.Whisper.TinyEnglishCheckpoint;

namespace Aurelian.Inference.Whisper;

internal sealed class WhisperTimestampRules(int samples)
{
    private const int FirstTimestamp = NoTimestamps + 1;
    private const int Space = 220;
    private readonly int finalTimestamp = FirstTimestamp + (samples + 319) / 320;
    private int count;
    private int last;
    private int previous;
    private int minimumTimestamp = FirstTimestamp;

    public void Push(int token)
    {
        previous = last;
        last = token;
        count++;
        if (token >= FirstTimestamp)
        {
            minimumTimestamp = token;
        }
    }

    public int Select(ReadOnlySpan<float> logits)
    {
        bool lastIsTimestamp = count > 0 && last >= FirstTimestamp;
        bool opensSegment = lastIsTimestamp && (count == 1 || previous >= FirstTimestamp);
        bool closesSegment = lastIsTimestamp && !opensSegment;
        foreach (float score in logits)
        {
            if (!float.IsFinite(score))
            {
                throw new InvalidDataException("Whisper decoder produced nonfinite logits.");
            }
        }

        int bestText = EndOfText;
        if (count > 0 && !closesSegment)
        {
            for (int token = 0; token < EndOfText; token++)
            {
                if ((token != Space || !opensSegment) && logits[token] > logits[bestText])
                {
                    bestText = token;
                }
            }
        }
        if (opensSegment)
        {
            return bestText;
        }

        int first = minimumTimestamp + (lastIsTimestamp || count == 0 ? 0 : 1);
        int end = count == 0 ? Math.Min(finalTimestamp, FirstTimestamp + 50) : finalTimestamp;
        if (first > end)
        {
            return bestText;
        }
        int timestamp = first;
        for (int token = first + 1; token <= end; token++)
        {
            if (logits[token] > logits[timestamp])
            {
                timestamp = token;
            }
        }
        if (count == 0)
        {
            return timestamp;
        }

        double sum = 0;
        for (int token = first; token <= end; token++)
        {
            sum += Math.Exp(logits[token] - logits[timestamp]);
        }
        return logits[timestamp] + Math.Log(sum) > logits[bestText] ? timestamp : bestText;
    }
}
