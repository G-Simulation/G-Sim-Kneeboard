const http = require('http');

const url = process.argv[2] || 'http://localhost:815/api/navigraph/procedure/KSJC/I12R?type=APPROACH';

http.get(url, res => {
    let data = '';
    res.on('data', chunk => data += chunk);
    res.on('end', () => {
        if (data.charCodeAt(0) === 0xFEFF) data = data.slice(1);
        console.log('Status:', res.statusCode);
        try {
            const json = JSON.parse(data);
            console.log(JSON.stringify(json, null, 2));
        } catch(e) {
            console.log(data);
        }
    });
}).on('error', e => console.log('Error:', e.message));
