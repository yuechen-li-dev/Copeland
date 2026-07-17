type Ready = () => boolean;
function ready(): boolean { return true; }
function main(): boolean { const check: Ready = ready; return check(); }
