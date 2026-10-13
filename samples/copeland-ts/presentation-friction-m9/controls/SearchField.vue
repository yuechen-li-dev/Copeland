<script setup lang="ts">
defineProps<{ id: string; type?: string; value: string }>();
const emit = defineEmits<{ valueChange: [value: string] }>();

function changed(event: Event): void {
    const input = event.target;
    if (input instanceof HTMLInputElement) {
        emit("valueChange", input.value);
    }
}
</script>

<template>
    <input :id="id" :type="type ?? 'text'" :value="value" @input="changed" />
</template>
