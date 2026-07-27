export default defineDatabase(
    <Database name="Events">
        <Index field="tenant">
            <Index field="year">
                <Table type={Event} />
            </Index>
        </Index>
    </Database>
);
