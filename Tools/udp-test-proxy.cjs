// Local-only packet impairment for the two-process test. Never proxies an external host.
const dgram = require('node:dgram');
const listenPort = Number(process.argv[2]);
const hostPort = Number(process.argv[3]);
const latency = Number(process.argv[4] || 75);
const loss = Number(process.argv[5] || 1);
if (![listenPort, hostPort].every(p => Number.isInteger(p) && p >= 1024 && p <= 65535) || listenPort === hostPort || !Number.isFinite(latency) || latency < 0 || latency > 500 || !Number.isFinite(loss) || loss < 0 || loss > 10) throw new Error('Invalid bounded local proxy settings');
const client = dgram.createSocket('udp4');
const server = dgram.createSocket('udp4');
let peer, received = 0, dropped = 0, forwarded = 0, seed = 8171;
function random() { seed = (Math.imul(seed, 1664525) + 1013904223) >>> 0; return seed / 4294967296; }
function queue(socket, packet, port) {
  received++;
  if (random() * 100 < loss) { dropped++; return; }
  const delay = Math.max(0, latency + Math.round((random() - .5) * 20));
  setTimeout(() => socket.send(packet, port, '127.0.0.1', error => { if (error) console.error(error.message); else forwarded++; }), delay);
}
client.on('message', (packet, remote) => {
  if (remote.address !== '127.0.0.1' || (peer && peer !== remote.port)) return;
  peer = remote.port; queue(server, packet, hostPort);
});
server.on('message', (packet, remote) => {
  if (remote.address === '127.0.0.1' && remote.port === hostPort && peer) queue(client, packet, peer);
});
client.on('error', error => { console.error(error); process.exit(1); });
server.on('error', error => { console.error(error); process.exit(1); });
server.bind(0, '127.0.0.1', () => client.bind(listenPort, '127.0.0.1', () => console.log(`READY ${listenPort}->${hostPort} delay=${latency}ms/leg loss=${loss}%`)));
const report = setInterval(() => console.log(`PACKETS received=${received} forwarded=${forwarded} dropped=${dropped}`), 5000);
function close() { clearInterval(report); console.log(`FINAL received=${received} forwarded=${forwarded} dropped=${dropped}`); client.close(); server.close(); setTimeout(() => process.exit(0), 50); }
process.on('SIGTERM', close); process.on('SIGINT', close);
setTimeout(close, 120000);
