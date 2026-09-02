const $schema: string = "copeland://tiny-farm/tests/m7/valid/routes";
record table SceneRoutes {
    routeId: string = [];
    sourceScene: string = [];
    triggerObject: string = [];
    targetScene: string = [];
    targetAnchor: string = [];
    interactionLabel: string = [];
}
const $value = SceneRoutes;
