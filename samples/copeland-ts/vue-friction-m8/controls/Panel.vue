<script setup lang="ts">
defineProps<{ label: string }>();
</script>

<template>
    <section :aria-label="label"><slot /></section>
</template>
