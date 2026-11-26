#include "whisper.h"

#include <chrono>
#include <cmath>
#include <cstdio>
#include <fstream>
#include <iomanip>
#include <iostream>
#include <memory>
#include <stdexcept>
#include <string>
#include <utility>
#include <vector>

namespace {
constexpr int mel_frames = 3000;
constexpr int mel_bins = 80;
constexpr int maximum_tokens = 64;
constexpr int threads = 4;
using Clock = std::chrono::steady_clock;

double elapsed_ms(Clock::time_point begin) {
    return std::chrono::duration<double, std::milli>(Clock::now() - begin).count();
}

int evaluate(whisper_context * context, whisper_token token, int past) {
    if (whisper_decode(context, &token, 1, past, threads) != 0) {
        throw std::runtime_error("whisper_decode failed");
    }
    const float * logits = whisper_get_logits(context);
    const int end = whisper_token_eot(context);
    int next = 0;
    for (int index = 0; index <= end; ++index) {
        if (!std::isfinite(logits[index])) {
            throw std::runtime_error("whisper.cpp produced non-finite logits");
        }
        if (logits[index] > logits[next]) {
            next = index;
        }
    }
    return next;
}

struct Sample {
    double total_ms;
    double encode_ms;
    double prefill_ms;
    double decode_ms;
    std::vector<int> tokens;
};

Sample transcribe(whisper_context * context, const std::vector<float> & mel) {
    const auto begin = Clock::now();
    if (whisper_set_mel(context, mel.data(), mel_frames, mel_bins) != 0 ||
        whisper_encode(context, 0, threads) != 0) {
        throw std::runtime_error("whisper.cpp could not encode the mel window");
    }
    const double encode = elapsed_ms(begin);
    const auto prefill_begin = Clock::now();
    evaluate(context, whisper_token_sot(context), 0);
    int next = evaluate(context, whisper_token_not(context), 1);
    const double prefill = elapsed_ms(prefill_begin);
    const auto decode_begin = Clock::now();
    std::vector<int> tokens;
    tokens.reserve(maximum_tokens);
    for (int index = 0; index < maximum_tokens; ++index) {
        tokens.push_back(next);
        if (next == whisper_token_eot(context)) {
            return {elapsed_ms(begin), encode, prefill, elapsed_ms(decode_begin), std::move(tokens)};
        }
        if (index + 1 < maximum_tokens) {
            next = evaluate(context, next, index + 2);
        }
    }
    throw std::runtime_error("whisper.cpp did not emit end-of-text within the benchmark token budget");
}

std::vector<float> read_mel(const char * path) {
    std::vector<float> frames(mel_frames * mel_bins);
    const auto byte_count = static_cast<std::streamsize>(frames.size() * sizeof(float));
    std::ifstream input(path, std::ios::binary);
    if (!input.read(reinterpret_cast<char *>(frames.data()), byte_count) || input.peek() != EOF) {
        throw std::runtime_error("expected a 3,000 x 80 frame-major little-endian FP32 mel fixture");
    }
    // whisper_set_mel takes mel-major storage. Prepare before the timed request.
    std::vector<float> mel(frames.size());
    for (int bin = 0; bin < mel_bins; ++bin) {
        for (int frame = 0; frame < mel_frames; ++frame) {
            mel[bin * mel_frames + frame] = frames[frame * mel_bins + bin];
        }
    }
    return mel;
}

void write_samples(std::ostream & output, const std::vector<Sample> & samples) {
    output << std::setprecision(10) << "[\n";
    for (size_t index = 0; index < samples.size(); ++index) {
        const auto & sample = samples[index];
        if (index != 0) {
            output << ",\n";
        }
        output << "  {\"TotalMs\":" << sample.total_ms << ",\"EncodeMs\":" << sample.encode_ms
               << ",\"PrefillMs\":" << sample.prefill_ms << ",\"DecodeMs\":" << sample.decode_ms
               << ",\"Tokens\":[";
        for (size_t token = 0; token < sample.tokens.size(); ++token) {
            if (token != 0) {
                output << ',';
            }
            output << sample.tokens[token];
        }
        output << "]}";
    }
    output << "\n]";
}
}

int main(int argc, char ** argv) {
    if (argc != 6) {
        std::cerr << "Usage: whisper-benchmark-reference CHECKPOINT MEL_F32 OUTPUT_JSON SAMPLES flash|standard\n";
        return 2;
    }
    try {
        const int count = std::stoi(argv[4]);
        const std::string attention = argv[5];
        if (count < 1 || count > 100 || (attention != "flash" && attention != "standard")) {
            throw std::runtime_error("expected 1–100 samples and flash/standard attention mode");
        }
        const auto mel = read_mel(argv[2]);
        auto params = whisper_context_default_params();
        params.use_gpu = true;
        params.flash_attn = attention == "flash";
        auto begin = Clock::now();
        std::unique_ptr<whisper_context, decltype(&whisper_free)> context(
            whisper_init_from_file_with_params(argv[1], params), whisper_free);
        const double initialization_ms = elapsed_ms(begin);
        if (!context || whisper_n_vocab(context.get()) != 51864) {
            throw std::runtime_error("could not load the Tiny.en checkpoint");
        }
        std::vector<Sample> warmup;
        std::vector<Sample> samples;
        for (int index = -2; index < count; ++index) {
            auto sample = transcribe(context.get(), mel);
            std::cerr << "Iteration " << index + 1 << ": " << sample.total_ms
                      << " ms, " << sample.tokens.size() << " outputs\n";
            (index < 0 ? warmup : samples).push_back(std::move(sample));
        }
        std::ofstream output(argv[3]);
        output << "{\"Runtime\":\"whisper.cpp\",\"Mode\":\"" << attention
               << "\",\"InitializationMs\":" << initialization_ms << ",\"Warmup\":";
        write_samples(output, warmup);
        output << ",\"Samples\":";
        write_samples(output, samples);
        output << "}\n";
        if (!output) {
            throw std::runtime_error("could not write benchmark output");
        }
        whisper_print_timings(context.get());
    } catch (const std::exception & error) {
        std::cerr << error.what() << '\n';
        return 1;
    }
}
