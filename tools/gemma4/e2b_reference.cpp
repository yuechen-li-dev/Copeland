#include "llama.h"

#include <chrono>
#include <fstream>
#include <iomanip>
#include <iostream>
#include <memory>
#include <sstream>
#include <stdexcept>
#include <string>
#include <vector>

namespace {
constexpr int vocabulary = 262144;
using Clock = std::chrono::steady_clock;

std::vector<std::vector<llama_token>> read_sequence(const char * path) {
    std::ifstream file(path);
    std::vector<std::vector<llama_token>> sequence;
    int total = 0;
    for (std::string line; std::getline(file, line);) {
        std::istringstream reader(line);
        std::vector<llama_token> tokens;
        for (int token; reader >> token;) {
            if (token < 0 || token >= vocabulary || ++total > 1024) {
                throw std::runtime_error("Token sequence exceeds the E2B profile");
            }
            tokens.push_back(token);
        }
        if (tokens.empty()) {
            continue;
        }
        if (!sequence.empty() && tokens.size() != 1) {
            throw std::runtime_error("Decode lines must contain one token");
        }
        sequence.push_back(std::move(tokens));
    }
    if (sequence.size() < 2) {
        throw std::runtime_error("A prefill and decode token are required");
    }
    return sequence;
}
}

int main(int argc, char ** argv) {
    if (argc != 4 && (argc != 5 || std::string(argv[4]) != "fp32")) {
        std::cerr << "Usage: gemma-e2b-reference CHECKPOINT TOKENS_FILE OUTPUT_PREFIX [fp32]\n";
        return 2;
    }
    try {
        const bool fp32 = argc == 5;
        auto sequence = read_sequence(argv[2]);
        ggml_backend_load_all();
        llama_backend_init();
        auto gpu = ggml_backend_dev_by_name("Vulkan0");
        if (!gpu) {
            throw std::runtime_error("Vulkan0 is unavailable");
        }
        ggml_backend_dev_t devices[] = {gpu, nullptr};
        auto model_params = llama_model_default_params();
        model_params.devices = devices;
        model_params.n_gpu_layers = 999;
        model_params.split_mode = LLAMA_SPLIT_MODE_NONE;
        model_params.lazy_mode = LLAMA_LAZY_MODE_OFF;
        std::unique_ptr<llama_model, decltype(&llama_model_free)> model(
            llama_model_load_from_file(argv[1], model_params), llama_model_free);
        if (!model) {
            throw std::runtime_error("Could not load the E2B checkpoint");
        }
        auto params = llama_context_default_params();
        params.n_batch = params.n_ubatch = static_cast<uint32_t>(sequence[0].size());
        params.n_ctx = params.n_batch + static_cast<uint32_t>(sequence.size() - 1);
        params.n_threads = params.n_threads_batch = 8;
        params.type_k = params.type_v = fp32 ? GGML_TYPE_F32 : GGML_TYPE_F16;
        params.flash_attn_type = fp32 ? LLAMA_FLASH_ATTN_TYPE_DISABLED : LLAMA_FLASH_ATTN_TYPE_ENABLED;
        params.no_perf = true;
        std::unique_ptr<llama_context, decltype(&llama_free)> context(llama_init_from_model(model.get(), params), llama_free);
        if (!context) {
            throw std::runtime_error("Could not create the E2B context");
        }

        std::string output(argv[3]);
        std::ofstream timing(output + ".json");
        std::ofstream logits(output + ".f32", std::ios::binary);
        timing << std::setprecision(10) << "{\"FP32Cache\":" << (fp32 ? "true" : "false") << ",\"Runs\":[";
        for (int run = 0; run < 4; ++run) {
            llama_memory_clear(llama_get_memory(context.get()), false);
            double prefill = 0;
            double decode = 0;
            if (run == 1) {
                timing << '\n';
            } else if (run > 1) {
                timing << ",\n";
            }
            if (run > 0) {
                timing << '[';
            }
            for (size_t step = 0; step < sequence.size(); ++step) {
                auto & tokens = sequence[step];
                auto start = Clock::now();
                if (llama_decode(context.get(), llama_batch_get_one(tokens.data(), static_cast<int>(tokens.size()))) != 0) {
                    throw std::runtime_error("llama_decode failed");
                }
                const float * scores = llama_get_logits_ith(context.get(), -1);
                double elapsed = std::chrono::duration<double, std::milli>(Clock::now() - start).count();
                if (!scores) {
                    throw std::runtime_error("Missing last-token logits");
                }
                if (run == 1) {
                    logits.write(reinterpret_cast<const char *>(scores), sizeof(float) * vocabulary);
                }
                if (run > 0) {
                    if (step) { timing << ','; }
                    timing << "{\"WallMs\":" << elapsed << '}';
                }
                if (step == 0) { prefill = elapsed; } else { decode += elapsed; }
            }
            if (run > 0) { timing << ']'; }
            std::cout << "Run " << run << ": prefill " << prefill << " ms, decode " << decode << " ms\n";
        }
        timing << "]}\n";
        if (!timing || !logits) {
            throw std::runtime_error("Could not write E2B evidence");
        }
    } catch (const std::exception & error) {
        std::cerr << error.what() << '\n';
        return 1;
    }
}
