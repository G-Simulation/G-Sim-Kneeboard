const http = require('http');

http.get('http://localhost:815/api/navigraph/approach-debug/KSJC/I12R', res => {
    let data = '';
    res.on('data', chunk => data += chunk);
    res.on('end', () => {
        if (data.charCodeAt(0) === 0xFEFF) data = data.slice(1);
        const j = JSON.parse(data);
        console.log('=== I12R Approach Legs ===');
        console.log('seqno | waypoint | route_type | path_term | lat | lon');
        console.log('------|----------|------------|-----------|-----|----');
        j.Legs.forEach(l => {
            console.log(`${l.seqno} | ${l.waypoint_identifier || 'null'} | ${l.route_type} | ${l.path_termination} | ${l.waypoint_latitude?.toFixed(4) || 0} | ${l.waypoint_longitude?.toFixed(4) || 0}`);
        });
        
        console.log('\n=== Analysis ===');
        console.log('Route types: I=ILS, F=Final, Z=Missed Approach');
        console.log('Path terms: IF=Initial Fix, TF=Track to Fix, CF=Course to Fix, CA=Course to Altitude');
    });
}).on('error', e => console.log('Error:', e.message));
