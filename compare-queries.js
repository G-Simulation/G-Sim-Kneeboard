const http = require('http');

async function fetchJson(url) {
    return new Promise((resolve, reject) => {
        http.get(url, res => {
            let data = '';
            res.on('data', chunk => data += chunk);
            res.on('end', () => {
                if (data.charCodeAt(0) === 0xFEFF) data = data.slice(1);
                try {
                    resolve(JSON.parse(data));
                } catch(e) {
                    reject(new Error(`Parse error: ${e.message}, raw: ${data.substring(0,200)}`));
                }
            });
        }).on('error', reject);
    });
}

(async () => {
    console.log('=== COMPARING APPROACH QUERIES FOR KSJC/I12R ===\n');
    
    try {
        // 1. Raw approach legs (no filter)
        const raw = await fetchJson('http://localhost:815/api/navigraph/approach-debug/KSJC/I12R');
        console.log('1. RAW APPROACH LEGS (no filter):');
        console.log(`   Total: ${raw.TotalLegs} legs`);
        raw.Legs?.forEach(l => console.log(`   - seqno=${l.seqno} ${l.waypoint_identifier || '(null)'} route=${l.route_type}`));
        
        console.log('');
        
        // 2. Test query (exact GetProcedureLegs SQL with route_type NOT IN filter)
        const test = await fetchJson('http://localhost:815/api/navigraph/approach-test/KSJC/I12R');
        console.log('2. TEST QUERY (route_type NOT IN A,Z):');
        console.log(`   Total: ${test.TotalLegs} legs`);
        test.Legs?.forEach(l => console.log(`   - seqno=${l.seqno} ${l.waypoint_identifier || '(null)'} route=${l.route_type}`));
        
        console.log('');
        
        // 3. Direct GetProcedureLegs call
        const legs = await fetchJson('http://localhost:815/api/navigraph/approach-legs/KSJC/I12R');
        console.log('3. DIRECT GetProcedureLegs (no filtering):');
        console.log(`   Total: ${legs.TotalLegs} legs`);
        legs.Legs?.forEach(l => console.log(`   - seqno=${l.SequenceNumber} ${l.WaypointIdentifier} route=${l.RouteType} lat=${l.Latitude}`));
        
        console.log('');
        
        // 4. Normal procedure endpoint
        const proc = await fetchJson('http://localhost:815/api/navigraph/procedure/KSJC/I12R?type=APPROACH');
        console.log('4. NORMAL PROCEDURE ENDPOINT (with all filters):');
        console.log(`   Total: ${proc.Waypoints?.length || 0} waypoints`);
        proc.Waypoints?.forEach(w => console.log(`   - seqno=${w.SequenceNumber} ${w.Identifier} route=${w.RouteType}`));
        
        console.log('');
        
        // 5. NEW: Diagnostic endpoint with debug log
        const diag = await fetchJson('http://localhost:815/api/navigraph/approach-diag/KSJC/I12R');
        console.log('5. DIAGNOSTIC GetProcedureLegs (with debug log):');
        console.log(`   Total: ${diag.TotalLegs} legs`);
        console.log('   Debug log:');
        diag.DebugLog?.forEach(line => console.log(`     ${line}`));
        
        console.log('\n=== ANALYSIS ===');
        console.log(`Raw SQL: ${raw.TotalLegs} legs`);
        console.log(`SQL with filter: ${test.TotalLegs} legs`);
        console.log(`GetProcedureLegs: ${legs.TotalLegs} legs`);
        console.log(`Diagnostic: ${diag.TotalLegs} legs`);
        console.log(`Final endpoint: ${proc.Waypoints?.length || 0} waypoints`);
        
        if (diag.TotalLegs < test.TotalLegs) {
            console.log('>>> Problem found in reader loop - check DebugLog above');
        } else if (legs.TotalLegs < diag.TotalLegs) {
            console.log('>>> Original GetProcedureLegs differs from Diagnostic version');
        } else if (proc.Waypoints?.length < legs.TotalLegs) {
            console.log('>>> Problem is in GetProcedureDetail (FilterToSinglePath or other post-processing)');
        }
        
    } catch(e) {
        console.log('Error:', e.message);
    }
})();
