using System;
using HelloCopeland;

export enum GreetingStyle { Friendly, Formal }

export record Greeting { recipient: string; message: string; }

export function greeting(name: string): string {
    return String.Concat(Helper.Decorate(name), " through System.String");
}
