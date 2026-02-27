// src/utils/utils.js

export function timestampToTime(timestamp) {
  if(typeof timestamp === 'string') {
    timestamp = parseInt(timestamp);
  }
  timestamp = timestamp ? timestamp : null;
  console.log(typeof timestamp);
  console.log(timestamp);
  const date = new Date(timestamp);
  console.log(date)
  const Y = date.getFullYear() + '-';
  const M = (date.getMonth() + 1 < 10 ? '0' : '') + (date.getMonth() + 1) + '-';
  const D = (date.getDate() < 10 ? '0' : '') + date.getDate() + ' ';
  const h = (date.getHours() < 10 ? '0' : '') + date.getHours() + ':';
  const m = (date.getMinutes() < 10 ? '0' : '') + date.getMinutes() + ':';
  const s = (date.getSeconds() < 10 ? '0' : '') + date.getSeconds();
  return Y + M + D + h + m + s;
}

export function jsonToText(msg) {
  try {
    const obj = typeof msg === 'string' ? JSON.parse(msg) : msg;
    if (typeof obj === 'object' && obj !== null) {
      return JSON.stringify(obj, null, 2);
    }
    return String(obj);
  } catch (e) {
    return msg;
  }
}
