<script setup lang="ts">
import { computed, ref } from "vue";
import { Toggle } from "reka-ui";
import SearchField from "../controls/SearchField.vue";
import { IssueOne, IssueTwo, IssueThree, IssueExtra, IssueDescription, ShortCopy, LongCopy } from "../src/Content";

interface Issue {
    id: number;
    title: string;
}
type Status = { kind: "ready" } | { kind: "loading" } | { kind: "failed"; message: string };
const rows = ref<Issue[]>([
    { id: 1, title: IssueOne() },
    { id: 2, title: IssueTwo() },
    { id: 3, title: IssueThree() },
]);
const nextId = ref(4);
const selected = ref<number | undefined>(1);
const draft = ref("");
const query = ref("");
const stacked = ref(false);
const longCopy = ref(false);
const status = ref<Status>({ kind: "ready" });
const visible = computed(() => rows.value.filter(issue => issue.title.includes(query.value)));
const detail = computed(() => rows.value.find(issue => issue.id === selected.value));
const prose = computed(() => longCopy.value ? LongCopy() : ShortCopy());

function insert(): void {
    const id = nextId.value++;
    const title = id === 4 ? IssueExtra() : `${IssueExtra()} / ${id}`;
    rows.value.push({ id, title });
}

function remove(): void {
    rows.value = rows.value.filter(issue => issue.id !== selected.value);
    selected.value = undefined;
}

function reverse(): void {
    rows.value.reverse();
}
</script>

<template>
    <section>
        <header class="panel">
            <h1>Issue triage</h1>
            <label for="search">Search issue titles</label>
            <SearchField id="search" type="search" :value="draft" @value-change="draft = $event" />
            <button @click="longCopy = !longCopy">Change copy length</button>
            <button @click="query = draft">Search</button>
            <button @click="insert">Insert issue</button>
            <button @click="remove">Remove selected issue</button>
            <button @click="reverse">Reverse issues</button>
            <button @click="status = { kind: 'loading' }">Show loading</button>
            <button @click="status = { kind: 'failed', message: 'Could not load issues.' }">Show error</button>
            <button @click="status = { kind: 'ready' }">Show ready</button>
        </header>
        <Toggle v-model="stacked">Change arrangement</Toggle>
        <div class="baseline-issues" :class="{ stacked }">
            <section v-if="status.kind === 'loading'" class="panel" role="status">Loading issues…</section>
            <section v-else-if="status.kind === 'failed'" class="panel" role="alert">
                {{ status.message }}
                <button @click="status = { kind: 'ready' }">Retry</button>
            </section>
            <section v-else-if="visible.length === 0" class="panel" role="status">No matching issues.</section>
            <section v-else class="panel" aria-label="Issue list">
                <h2>Incoming issues</h2>
                <ul class="issue-list">
                    <li v-for="issue in visible" :key="issue.id">
                        <button :data-choice="issue.id" :aria-pressed="selected === issue.id" @click="selected = issue.id">
                            {{ issue.title }}
                        </button>
                    </li>
                </ul>
            </section>
            <article v-if="detail" id="issue-detail" class="panel reading">
                <p class="eyebrow">SELECTED ISSUE {{ detail.id }}</p>
                <h2>{{ detail.title }}</h2>
                <p class="readable">{{ IssueDescription() }}</p>
                <p class="readable" data-prose="detail">{{ prose }}</p>
                <a href="#app">Return to navigation</a>
            </article>
            <section v-else class="panel" role="status">Select an issue to read its details.</section>
        </div>
    </section>
</template>

<style scoped>
.baseline-issues {
    display: grid;
    grid-template-columns: 300px minmax(0, 1fr);
    background: #e8eef0;
}
.baseline-issues.stacked {
    grid-template-columns: minmax(0, 1fr);
}
@media (max-width: 599px) {
    .baseline-issues {
        grid-template-columns: minmax(0, 1fr);
    }
}
</style>
