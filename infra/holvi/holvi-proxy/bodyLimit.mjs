const DEFAULT_MAX_BODY_BYTES = 256 * 1024; // 256KB

async function readJsonWithLimit(req, maxSize = DEFAULT_MAX_BODY_BYTES) {
  const contentLength = Number(req.headers['content-length'] || 0);

  // Fast-fail if content-length header exceeds limit
  if (contentLength > maxSize) {
    throw new Error(`Request body too large (content-length: ${contentLength}, max: ${maxSize})`);
  }

  let bytes = Buffer.alloc(0);

  return new Promise((resolve, reject) => {
    req.on('data', (chunk) => {
      bytes = Buffer.concat([bytes, chunk]);
      if (bytes.length > maxSize) {
        reject(new Error(`Request body too large: ${bytes.length} > ${maxSize}`));
        req.pause(); // Stop reading
      }
    });

    req.on('end', () => {
      try {
        const body = bytes.length > 0 ? JSON.parse(bytes.toString('utf8')) : {};
        resolve(body);
      } catch (e) {
        reject(new Error('Invalid JSON body'));
      }
    });

    req.on('error', reject);
  });
}

export { readJsonWithLimit };
