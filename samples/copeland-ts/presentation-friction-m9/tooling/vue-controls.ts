import { defineComponent, h, type PropType } from "vue";
import { Toggle } from "reka-ui";
export { default as SearchField } from "../controls/SearchField.vue";
export { default as Panel } from "../controls/Panel.vue";

export const ArrangementToggle = defineComponent({
    props: {
        value: { type: Boolean, required: true },
        onValueChange: {
            type: Function as PropType<(value: boolean) => void>,
            required: true,
        },
    },
    setup(props, { slots }) {
        return () =>
            h(
                Toggle,
                {
                    modelValue: props.value,
                    "onUpdate:modelValue": props.onValueChange,
                },
                slots,
            );
    },
});
