const http = require('http');

const url = 'http://localhost:815/api/navigraph/approach-debug/KSJC/I12R';

http.get(url, res => {
    let data = '';
    res.on('data', chunk => data += chunk);
    res.on('end', () => {
        if (data.charCodeAt(0) === 0xFEFF) data = data.slice(1);
        const json = JSON.parse(data);
        console.log('=== I12R RAW APPROACH LEGS ===');
        console.log('Total:', json.TotalLegs);
        console.log('\nLegs sorted by seqno:');
        
        const legs = json.Legs.sort((a, b) => (a.seqno || 0) - (b.seqno || 0));
        legs.forEach((leg, i) => {
            const hasCoords = leg.waypoint_latitude && leg.waypoint_longitude && 
                              Math.abs(leg.waypoint_latitude) > 0.001;
            console.log(`${i+1}. seqno=${leg.seqno} ${leg.waypoint_identifier || '(null)'} route=${leg.route_type} path=${leg.path_termination} coords=${hasCoords ? 'YES' : 'NO'} lat=${leg.waypoint_latitude}`);
        });
        
        console.log('\n=== Would pass 0,0 filter: ===');
        legs.filter(l => l.waypoint_latitude && Math.abs(l.waypoint_latitude) > 0.001)
            .forEach(l => console.log(`  ${l.seqno}: ${l.waypoint_identifier} (${l.waypoint_latitude}, ${l.waypoint_longitude})`));
    });
}).on('error', e => console.log('Error:', e.message));
