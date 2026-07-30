export function getMountElement(id) {
  const element = document.getElementById(id);
  if (element === null) throw new Error("Copeland React host could not find mount element '" + id + "'.");
  return element;
}
