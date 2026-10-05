import { dotnet } from './_framework/dotnet.js';

const status = document.querySelector('#status');
try {
    const runtime = await dotnet.create();
    const exports = await runtime.getAssemblyExports('WgslQualification');
    const compile = (source, path) => JSON.parse(exports.BrowserQualification.Compile(source, path));
    if (!navigator.gpu) throw new Error('WebGPU is unavailable.');
    const adapter = await navigator.gpu.requestAdapter();
    if (!adapter) throw new Error('No WebGPU adapter.');
    const device = await adapter.requestDevice();
    const directOnly = new URLSearchParams(location.search).has('directOnly');
    document.querySelector('#comparison').textContent = directOnly
        ? 'Direct path only: no oracle artifacts are requested.'
        : 'Each pair: direct WGSL, then the retained precompiled DXC/Naga oracle.';
    const oracle = directOnly ? null : await (await fetch('oracle.json')).json();
    const format = navigator.gpu.getPreferredCanvasFormat();
    const positions = new Float32Array([-1, -1, 0, 1, -1, 0, -1, 1, 0, -1, 1, 0, 1, -1, 0, 1, 1, 0]);
    const vertexBuffer = device.createBuffer({ size: positions.byteLength, usage: GPUBufferUsage.VERTEX | GPUBufferUsage.COPY_DST });
    device.queue.writeBuffer(vertexBuffer, 0, positions);
    const results = [];

    async function shader(code, mappings = []) {
        const start = performance.now();
        const module = device.createShaderModule({ code });
        const info = await module.getCompilationInfo();
        const diagnostics = info.messages.map(message => ({
            type: message.type, line: message.lineNum, column: message.linePos, message: message.message,
            source: mappings.filter(mapping => mapping.line <= message.lineNum).at(-1)?.source,
        }));
        return { module, diagnostics, milliseconds: performance.now() - start };
    }

    async function render(label, vertex, fragment, vertexEntry, fragmentEntry, buffer, attributes, stride, bindings) {
        const section = document.createElement('section');
        const heading = document.createElement('h2');
        heading.textContent = label;
        const canvas = document.createElement('canvas');
        canvas.width = canvas.height = 256;
        section.append(heading, canvas);
        document.querySelector('main').append(section);
        const context = canvas.getContext('webgpu');
        context.configure({ device, format, usage: GPUTextureUsage.RENDER_ATTACHMENT | GPUTextureUsage.COPY_SRC });
        device.pushErrorScope('validation');
        const pipelineStart = performance.now();
        const pipeline = await device.createRenderPipelineAsync({
            layout: 'auto',
            vertex: { module: vertex, entryPoint: vertexEntry, buffers: [{ arrayStride: stride, attributes }] },
            fragment: { module: fragment, entryPoint: fragmentEntry, targets: [{ format }] },
            primitive: { topology: 'triangle-list' },
            depthStencil: { format: 'depth32float', depthWriteEnabled: true, depthCompare: 'less' },
        });
        const pipelineMilliseconds = performance.now() - pipelineStart;
        const depth = device.createTexture({ size: [256, 256], format: 'depth32float', usage: GPUTextureUsage.RENDER_ATTACHMENT | GPUTextureUsage.COPY_SRC });
        const colorReadback = device.createBuffer({ size: 256 * 256 * 4, usage: GPUBufferUsage.COPY_DST | GPUBufferUsage.MAP_READ });
        const depthReadback = device.createBuffer({ size: 256 * 256 * 4, usage: GPUBufferUsage.COPY_DST | GPUBufferUsage.MAP_READ });
        const target = context.getCurrentTexture();
        const encoder = device.createCommandEncoder();
        const pass = encoder.beginRenderPass({
            colorAttachments: [{ view: target.createView(), clearValue: [0, 0, 0, 1], loadOp: 'clear', storeOp: 'store' }],
            depthStencilAttachment: { view: depth.createView(), depthClearValue: 1, depthLoadOp: 'clear', depthStoreOp: 'store' },
        });
        pass.setPipeline(pipeline);
        if (bindings) pass.setBindGroup(0, device.createBindGroup({ layout: pipeline.getBindGroupLayout(0), entries: bindings }));
        pass.setVertexBuffer(0, buffer);
        pass.draw(6);
        pass.end();
        encoder.copyTextureToBuffer({ texture: target }, { buffer: colorReadback, bytesPerRow: 1024 }, [256, 256]);
        encoder.copyTextureToBuffer({ texture: depth, aspect: 'depth-only' }, { buffer: depthReadback, bytesPerRow: 1024 }, [256, 256]);
        device.queue.submit([encoder.finish()]);
        await Promise.all([colorReadback.mapAsync(GPUMapMode.READ), depthReadback.mapAsync(GPUMapMode.READ)]);
        const color = new Uint8Array(colorReadback.getMappedRange()).slice();
        const depths = new Float32Array(depthReadback.getMappedRange()).slice();
        colorReadback.unmap();
        depthReadback.unmap();
        colorReadback.destroy();
        depthReadback.destroy();
        depth.destroy();
        const error = await device.popErrorScope();
        if (error) throw new Error(label + ': ' + error.message);
        return { color, depths, pipelineMilliseconds };
    }

    function compare(a, b) {
        let directHits = 0;
        let oracleHits = 0;
        let differentCoverage = 0;
        let common = 0;
        let depthError = 0;
        let maxDepthError = 0;
        let colorError = 0;
        let maxColorError = 0;
        for (let pixel = 0; pixel < a.depths.length; pixel++) {
            const ahit = a.depths[pixel] < 1;
            const bhit = b.depths[pixel] < 1;
            if (ahit) directHits++;
            if (bhit) oracleHits++;
            if (ahit !== bhit) differentCoverage++;
            if (ahit && bhit) {
                common++;
                const error = Math.abs(a.depths[pixel] - b.depths[pixel]);
                depthError += error;
                maxDepthError = Math.max(maxDepthError, error);
                for (let channel = 0; channel < 3; channel++) {
                    const difference = Math.abs(a.color[pixel * 4 + channel] - b.color[pixel * 4 + channel]);
                    colorError += difference;
                    maxColorError = Math.max(maxColorError, difference);
                }
            }
        }
        const meanDepthError = depthError / Math.max(common, 1);
        const meanColorError = colorError / Math.max(common * 3, 1);
        const samples = [128 * 256 + 128, 128 * 256 + 80, 80 * 256 + 128].map(pixel => ({
            pixel, directDepth: a.depths[pixel], oracleDepth: b.depths[pixel],
            directColor: Array.from(a.color.slice(pixel * 4, pixel * 4 + 4)),
            oracleColor: Array.from(b.color.slice(pixel * 4, pixel * 4 + 4)),
        }));
        const pass = directHits > 1000 && directHits < 60000 && differentCoverage <= 131 && meanDepthError < 0.00002 && maxDepthError < 0.0005 && meanColorError < 1;
        return { pass, directHits, oracleHits, differentCoverage, common, meanDepthError, maxDepthError, meanColorError, maxColorError, samples };
    }

    let firstProgram;
    for (const name of ['Sphere', 'Cylinder', 'Cone', 'Torus', 'CSG']) {
        status.textContent = 'Compiling and comparing ' + name + ' in browser WASM…';
        const source = await (await fetch(name + '.v.ts')).text();
        const start = performance.now();
        const result = compile(source, name + '.v.ts');
        const frontendAndBackendMilliseconds = performance.now() - start;
        if (!result.success) throw new Error(JSON.stringify(result.diagnostics));
        const program = result.program;
        firstProgram ??= program;
        const direct = await shader(program.code, program.sourceMappings);
        const oldVertex = directOnly ? null : await shader(oracle[name].Vertex);
        const oldFragment = directOnly ? null : await shader(oracle[name].Fragment);
        const errors = [...direct.diagnostics, ...(oldVertex?.diagnostics ?? []), ...(oldFragment?.diagnostics ?? [])].filter(message => message.type === 'error');
        if (errors.length) throw new Error(JSON.stringify({ name, errors }));
        const attributes = [{ shaderLocation: 0, offset: 0, format: 'float32x3' }];
        const a = await render(name + ' · direct', direct.module, direct.module, program.vertexEntryPoint, program.fragmentEntryPoint, vertexBuffer, attributes, 12);
        const b = directOnly ? null : await render(name + ' · oracle', oldVertex.module, oldFragment.module, 'VertexMain', 'PixelMain', vertexBuffer, attributes, 12);
        const differential = b ? compare(a, b) : null;
        const directHits = a.depths.filter(value => value < 1).length;
        if (directHits < 1000 || directHits > 60000 || a.depths.some(value => !Number.isFinite(value) || value < 0 || value > 1)) {
            throw new Error(name + ': invalid direct depth/coverage');
        }
        results.push({ name, semanticHash: program.semanticHash, directHits, differential, frontendAndBackendMilliseconds,
            directGenerationMilliseconds: program.generationMilliseconds, directModuleMilliseconds: direct.milliseconds,
            oracleModuleMilliseconds: oldVertex ? oldVertex.milliseconds + oldFragment.milliseconds : null,
            directPipelineMilliseconds: a.pipelineMilliseconds, oraclePipelineMilliseconds: b?.pipelineMilliseconds ?? null,
            oldDxcMilliseconds: oracle?.[name].DxcMs ?? null, oldNagaMilliseconds: oracle?.[name].TranslationMilliseconds ?? null,
            directBytes: new TextEncoder().encode(program.code).length,
            oracleBytes: oracle ? new TextEncoder().encode(oracle[name].Vertex + oracle[name].Fragment).length : null });
        if (differential && !differential.pass) throw new Error(JSON.stringify({ name, differential }));
    }

    // Exercise the real existing resource ABI, builtin streams and uniform bytes.
    const resourceSource = await (await fetch('ForwardTexturedM3.v.ts')).text();
    const resourceResult = compile(resourceSource, 'ForwardTexturedM3.v.ts');
    if (!resourceResult.success) throw new Error(JSON.stringify(resourceResult.diagnostics));
    const resourceProgram = resourceResult.program;
    const resourceShader = await shader(resourceProgram.code, resourceProgram.sourceMappings);
    if (resourceShader.diagnostics.some(message => message.type === 'error')) throw new Error(JSON.stringify(resourceShader.diagnostics));
    const interleaved = new Float32Array(6 * 5);
    for (let i = 0; i < 6; i++) {
        interleaved.set(positions.slice(i * 3, i * 3 + 3), i * 5);
        interleaved.set([0.5, 0.5], i * 5 + 3);
    }
    const resourceVertices = device.createBuffer({ size: interleaved.byteLength, usage: GPUBufferUsage.VERTEX | GPUBufferUsage.COPY_DST });
    device.queue.writeBuffer(resourceVertices, 0, interleaved);
    const texture = device.createTexture({ size: [1, 1], format: 'rgba8unorm', usage: GPUTextureUsage.TEXTURE_BINDING | GPUTextureUsage.COPY_DST });
    device.queue.writeTexture({ texture }, new Uint8Array([128, 255, 64, 255]), { bytesPerRow: 4 }, [1, 1]);
    const uniform = device.createBuffer({ size: 32, usage: GPUBufferUsage.UNIFORM | GPUBufferUsage.COPY_DST });
    device.queue.writeBuffer(uniform, 0, new Float32Array([0.3, 0.6, 0.9, 1, 0.7, 0, 0, 0]));
    const bindingRender = await render('Resources · direct', resourceShader.module, resourceShader.module,
        resourceProgram.vertexEntryPoint, resourceProgram.fragmentEntryPoint, resourceVertices,
        [{ shaderLocation: 0, offset: 0, format: 'float32x3' }, { shaderLocation: 1, offset: 12, format: 'float32x2' }], 20,
        [{ binding: 0, resource: texture.createView() }, { binding: 1, resource: device.createSampler() }, { binding: 2, resource: { buffer: uniform } }]);
    const centerColor = Array.from(bindingRender.color.slice((128 * 256 + 128) * 4, (128 * 256 + 128) * 4 + 4));
    const expected = format.startsWith('bgra') ? [58, 153, 38, 255] : [38, 153, 58, 255];
    if (centerColor.some((value, i) => Math.abs(value - expected[i]) > 1)) throw new Error('Resource ABI color mismatch: ' + centerColor);
    uniform.destroy();
    texture.destroy();
    resourceVertices.destroy();

    const assetValidation = [];
    for (const name of ['SoftShockwave', 'SemanticFog', 'ReactiveFluid2D', 'ProfileMsdf', 'MsdfText', 'AnalyticShape2D']) {
        const source = await (await fetch(name + '.v.ts')).text();
        const result = compile(source, name + '.v.ts');
        if (!result.success) throw new Error(JSON.stringify({ name, diagnostics: result.diagnostics }));
        const compiled = await shader(result.program.code, result.program.sourceMappings);
        if (compiled.diagnostics.some(message => message.type === 'error')) throw new Error(JSON.stringify({ name, diagnostics: compiled.diagnostics }));
        assetValidation.push({ name, semanticHash: result.program.semanticHash, diagnostics: compiled.diagnostics });
    }

    const failure = await shader(firstProgram.code.replace('sqrt(', 'vd_missing('), firstProgram.sourceMappings);
    const failureErrors = failure.diagnostics.filter(message => message.type === 'error');
    if (!failureErrors.length || !failureErrors[0].source) throw new Error('Expected actionable invalid-WGSL diagnostics were not surfaced.');
    vertexBuffer.destroy();
    const report = { status: 'PASS', browser: navigator.userAgent,
        gpu: { vendor: adapter.info.vendor, architecture: adapter.info.architecture, description: adapter.info.description },
        compilerHost: 'browser-wasm', directOnly, nativeCompilerDependencies: [], results,
        bindingWitness: { bindings: resourceProgram.semantics.resources, textureRgba: [128, 255, 64, 255], centerColor }, assetValidation, expectedFailure: failureErrors };
    globalThis.wgslQualification = report;
    status.textContent = JSON.stringify(report, null, 2);
} catch (error) {
    globalThis.wgslQualification = { status: 'FAIL', error: String(error) };
    status.textContent = JSON.stringify(globalThis.wgslQualification, null, 2);
}
