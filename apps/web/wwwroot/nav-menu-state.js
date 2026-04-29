const mediaKey = "mediacloud.nav.mediaExpanded";
const operationsKey = "mediacloud.nav.operationsExpanded";

function parseBool(value) {
  if (value === "true") return true;
  if (value === "false") return false;
  return null;
}

export function getMenuState() {
  return {
    mediaExpanded: parseBool(window.localStorage.getItem(mediaKey)),
    operationsExpanded: parseBool(window.localStorage.getItem(operationsKey))
  };
}

export function setMenuState(mediaExpanded, operationsExpanded) {
  window.localStorage.setItem(mediaKey, mediaExpanded ? "true" : "false");
  window.localStorage.setItem(operationsKey, operationsExpanded ? "true" : "false");
}
