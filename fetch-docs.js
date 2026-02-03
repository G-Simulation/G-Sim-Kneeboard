const https = require('https');

const url = 'https://developers.navigraph.com/docs/navigation-data/dfd-data-format-v2';

https.get(url, res => {
  let data = '';
  res.on('data', chunk => data += chunk);
  res.on('end', () => {
    // Extract text content, looking for approach/procedure related sections
    const text = data.replace(/<[^>]+>/g, ' ').replace(/\s+/g, ' ');
    
    console.log('Page length:', data.length);
    
    // Look for tbl_pf_iaps (Instrument Approach Procedures table)
    const iapsIdx = text.toLowerCase().indexOf('tbl_pf_iaps');
    if (iapsIdx > 0) {
      console.log('\n=== tbl_pf_iaps TABLE ===');
      console.log(text.substring(iapsIdx, iapsIdx + 3000));
    }
    
    // Look for path_termination
    const pathTermIdx = text.toLowerCase().indexOf('path_termination');
    if (pathTermIdx > 0) {
      console.log('\n=== PATH_TERMINATION ===');
      console.log(text.substring(pathTermIdx, pathTermIdx + 2000));
    }
  });
}).on('error', e => console.log('Error:', e.message));
