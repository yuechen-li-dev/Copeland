# Gemma reference provenance

The graph and offline oracle follow the published Hugging Face Transformers
Gemma4 Unified operation contracts (Apache-2.0):
https://github.com/huggingface/transformers/tree/main/src/transformers/models/gemma4_unified
and proportional RoPE in `src/transformers/modeling_rope_utils.py`.
Copyright 2026 The Hugging Face Team. The operation bindings and Vulkan kernels
here are independently authored against the shared Aurelian compute API.

The model configuration and tokenizer are Google's published Gemma 4 12B IT
assets. Local user-supplied weights and converted artifacts are excluded from
source control. Their filenames and structural contracts do not establish the
checkpoint's training variant. No model weights are redistributed.

Q4B32 is Aurelian's explicit post-training block representation with FP32 scales.
It is not Google QAT, GGUF or byte-compatible with GGUF's Q4_0.
