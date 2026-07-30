export function getMountElement(id) {
  const element = document.getElementById(id);
  if (element === null) throw new Error(`Missing browser mount element '${id}'.`);
  return element;
}
