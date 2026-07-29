// `DesktopLayout.generated.ts` is deterministic compiler output.
const layout = DesktopLayout;

export function LayoutFixture(): ReactNode {
    return (
        <main className={layout.root.className}>
            <aside className={layout.sidebar.className} />
            <section className={layout.main.className}>
                <div className={layout.hero.className} />
                <div className={layout.features.className} />
                <footer className={layout.footer.className} />
            </section>
        </main>
    );
}
