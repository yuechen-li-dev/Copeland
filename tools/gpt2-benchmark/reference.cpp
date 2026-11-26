#include "llama.h"

#include <chrono>
#include <cmath>
#include <fstream>
#include <iomanip>
#include <iostream>
#include <memory>
#include <numeric>
#include <stdexcept>
#include <string>
#include <vector>

namespace {
constexpr int prompt_length = 32;
constexpr int decode_steps = 32;
constexpr int vocabulary = 50257;
using Clock = std::chrono::steady_clock;

struct Prediction {
    int token;
    float score;
};

struct Sample {
    double prefill_ms;
    double decode_ms;
    std::vector<Prediction> predictions;
};

double elapsed(Clock::time_point start) {
    return std::chrono::duration<double, std::milli>(Clock::now() - start).count();
}

Prediction evaluate(llama_context * context, llama_token * ids, int count) {
    if (llama_decode(context, llama_batch_get_one(ids, count)) != 0) {
        throw std::runtime_error("llama_decode failed");
    }
    const float * logits = llama_get_logits_ith(context, -1);
    if (!logits) {
        throw std::runtime_error("missing last-token logits");
    }
    int best = 0;
    for (int index = 0; index < vocabulary; ++index) {
        if (!std::isfinite(logits[index])) {
            throw std::runtime_error("GPT-2 produced non-finite logits");
        }
        if (logits[index] > logits[best]) {
            best = index;
        }
    }
    return {best, logits[best]};
}

Sample run(llama_context * context) {
    llama_memory_clear(llama_get_memory(context), false);
    std::vector<llama_token> prompt(prompt_length);
    std::iota(prompt.begin(), prompt.end(), 100);
    Sample sample{};
    sample.predictions.reserve(decode_steps + 1);
    auto start = Clock::now();
    sample.predictions.push_back(evaluate(context, prompt.data(), prompt_length));
    sample.prefill_ms = elapsed(start);
    start = Clock::now();
    for (int step = 0; step < decode_steps; ++step) {
        llama_token id = 500 + step;
        sample.predictions.push_back(evaluate(context, &id, 1));
    }
    sample.decode_ms = elapsed(start);
    return sample;
}
}

int main(int argc, char ** argv) {
    if ((argc != 4 && argc != 5) || (argc == 5 && std::string(argv[4]) != "cpu")) {
        std::cerr << "Usage: gpt2-benchmark-reference CHECKPOINT OUTPUT_JSON SAMPLES [cpu]\n";
        return 2;
    }
    try {
        const int count = std::stoi(argv[3]);
        if (count < 1 || count > 100) {
            throw std::runtime_error("Expected 1 to 100 samples.");
        }
        ggml_backend_load_all();
        llama_backend_init();
        const bool use_gpu = argc == 4;
        auto gpu = ggml_backend_dev_by_name("Vulkan0");
        if (use_gpu && !gpu) {
            throw std::runtime_error("Vulkan0 is unavailable");
        }
        ggml_backend_dev_t devices[] = {gpu, nullptr};
        auto model_params = llama_model_default_params();
        model_params.devices = use_gpu ? devices : nullptr;
        model_params.n_gpu_layers = use_gpu ? 99 : 0;
        model_params.split_mode = LLAMA_SPLIT_MODE_NONE;
        std::unique_ptr<llama_model, decltype(&llama_model_free)> model(
            llama_model_load_from_file(argv[1], model_params), llama_model_free);
        if (!model) {
            throw std::runtime_error("could not load the GPT-2 checkpoint");
        }
        auto params = llama_context_default_params();
        params.n_ctx = prompt_length + decode_steps;
        params.n_batch = prompt_length;
        params.n_ubatch = prompt_length;
        params.n_threads = params.n_threads_batch = 4;
        params.type_k = params.type_v = GGML_TYPE_F32;
        params.flash_attn_type = LLAMA_FLASH_ATTN_TYPE_DISABLED;
        std::unique_ptr<llama_context, decltype(&llama_free)> context(llama_init_from_model(model.get(), params), llama_free);
        if (!context) {
            throw std::runtime_error("could not create the GPT-2 context");
        }

        std::ofstream output(argv[2]);
        output << std::setprecision(10) << "{\"Device\":\"" << (use_gpu ? ggml_backend_dev_description(gpu) : "CPU")
               << "\",\"Samples\":[\n";
        for (int index = -2; index < count; ++index) {
            const auto sample = run(context.get());
            if (index < 0) {
                continue;
            }
            if (index > 0) {
                output << ",\n";
            }
            output << "{\"PrefillMs\":" << sample.prefill_ms << ",\"DecodeMs\":" << sample.decode_ms
                   << ",\"Predictions\":[";
            for (size_t token = 0; token < sample.predictions.size(); ++token) {
                if (token > 0) {
                    output << ',';
                }
                const auto prediction = sample.predictions[token];
                output << "{\"Token\":" << prediction.token << ",\"Score\":" << prediction.score << '}';
            }
            output << "]}";
        }
        output << "\n]}\n";
        if (!output) {
            throw std::runtime_error("could not write benchmark output");
        }
    } catch (const std::exception & error) {
        std::cerr << error.what() << '\n';
        return 1;
    }
}
