# Changelog

## [1.11.0](https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers/compare/v1.10.0...v1.11.0) (2026-10-10)


### Features

* follow spans through calls and in arguments in ZA0110 ([8161f85](https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers/commit/8161f85c01d185cdbc858729946da7a361dca367))
* report overloaded params methods in ZA0110 when no overload can conflict ([8161f85](https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers/commit/8161f85c01d185cdbc858729946da7a361dca367))


### Bug Fixes

* check extension overloads in other static classes before ZA0110 reports ([8161f85](https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers/commit/8161f85c01d185cdbc858729946da7a361dca367))
* import System for ZA0110's fix on generic methods ([8161f85](https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers/commit/8161f85c01d185cdbc858729946da7a361dca367))
* point ZA1710's Mediator hint at the generated AddMediator ([#105](https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers/issues/105)) ([df408da](https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers/commit/df408da0278a189d8490ed7874d508995fbad442))
* stop ZA0110 treating a span passed to a ref struct instance method as used up ([8161f85](https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers/commit/8161f85c01d185cdbc858729946da7a361dca367))

## [1.10.0](https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers/compare/v1.9.0...v1.10.0) (2026-10-10)


### Features

* report rented buffers passed to async Stream calls in ZA0303 ([459496b](https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers/commit/459496be3c6d0e97961fa7c1a85b9df241390d1f))
* report using statements over a dynamic resource in ZA1709 ([459496b](https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers/commit/459496be3c6d0e97961fa7c1a85b9df241390d1f))


### Bug Fixes

* stop ZA0303 trusting methods a Stream subclass adds ([459496b](https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers/commit/459496be3c6d0e97961fa7c1a85b9df241390d1f))
* trust Array and Buffer methods that take System.Array in ZA0303 ([459496b](https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers/commit/459496be3c6d0e97961fa7c1a85b9df241390d1f))

## [1.9.0](https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers/compare/v1.8.0...v1.9.0) (2026-10-09)


### Features

* add ZA1710 to report assembly-scanning registration ([f77217b](https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers/commit/f77217b626ba531b165ff8823c329ae1b8516fa0))

## [1.8.0](https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers/compare/v1.7.0...v1.8.0) (2026-10-09)


### Features

* add ZA1706 to report libraries that never set IsAotCompatible ([77249c7](https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers/commit/77249c76cdb536ef38ed781f934ae5cbeb4f9ac5))
* add ZA1707 to suggest the configuration-binding source generator ([77249c7](https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers/commit/77249c76cdb536ef38ed781f934ae5cbeb4f9ac5))
* add ZA1708 to suggest OptionsValidator over ValidateDataAnnotations ([77249c7](https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers/commit/77249c76cdb536ef38ed781f934ae5cbeb4f9ac5))
* add ZA1709 to report dynamic dispatch ([77249c7](https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers/commit/77249c76cdb536ef38ed781f934ae5cbeb4f9ac5))

## [1.7.0](https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers/compare/v1.6.0...v1.7.0) (2026-10-09)


### Features

* add ZA1402 to suggest state-passing overloads over capturing lambdas ([4c7a135](https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers/commit/4c7a13593c7e5c3c1b5ae6947c81ae98e49878f0))
* add ZA1503 to report struct hash keys without IEquatable ([4c7a135](https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers/commit/4c7a13593c7e5c3c1b5ae6947c81ae98e49878f0))

## [1.6.0](https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers/compare/v1.5.6...v1.6.0) (2026-10-09)


### Features

* add ZA0110 to suggest declaring params as ReadOnlySpan ([bd67b56](https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers/commit/bd67b569dc93c2f3d01319537908cbcb4a5422bf))
* add ZA0111 to enumerate ConcurrentDictionary instead of Keys or Values snapshots ([bd67b56](https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers/commit/bd67b569dc93c2f3d01319537908cbcb4a5422bf))
* add ZA0210 to replace constant UTF-8 encoding with u8 literals ([bd67b56](https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers/commit/bd67b569dc93c2f3d01319537908cbcb4a5422bf))
* add ZA0211 to suggest the span-based Split on .NET 9 ([bd67b56](https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers/commit/bd67b569dc93c2f3d01319537908cbcb4a5422bf))
* add ZA0212 to prefer TryParse over catching Parse exceptions ([bd67b56](https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers/commit/bd67b569dc93c2f3d01319537908cbcb4a5422bf))
* add ZA0303 to report rented arrays never returned to the pool ([bd67b56](https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers/commit/bd67b569dc93c2f3d01319537908cbcb4a5422bf))
* add ZA0304 to turn constant lookup tables into ReadOnlySpan properties ([bd67b56](https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers/commit/bd67b569dc93c2f3d01319537908cbcb4a5422bf))


### Bug Fixes

* stop the ZA0109 code fix adding a redundant using System next to a global using ([bd67b56](https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers/commit/bd67b569dc93c2f3d01319537908cbcb4a5422bf))

## [1.5.6](https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers/compare/v1.5.5...v1.5.6) (2026-10-09)


### Documentation

* design batch 3 zero-alloc and NativeAOT analyzer rules ([#87](https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers/issues/87)) ([6a791c5](https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers/commit/6a791c537a641f18ba799222d5240407dfdeec77))

## [1.5.5](https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers/compare/v1.5.4...v1.5.5) (2026-10-09)


### Bug Fixes

* stop ZA0602 reporting params calls that do not allocate ([#71](https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers/issues/71)) ([b69fcbb](https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers/commit/b69fcbb695e721d13e3612acfbd79500f1d878d6)), closes [#70](https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers/issues/70)

## [1.5.4](https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers/compare/v1.5.3...v1.5.4) (2026-09-25)


### Bug Fixes

* mark released analyzer rules as shipped and automate the move on release ([#66](https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers/issues/66)) ([89adc3b](https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers/commit/89adc3b6d039b9f2a26b846b9f974887f960da7f))

## [1.5.3](https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers/compare/v1.5.2...v1.5.3) (2026-09-20)


### Bug Fixes

* **ci:** pin the SDK floor at the .NET 10 GA band, not the newest patch ([#61](https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers/issues/61)) ([8b9cb21](https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers/commit/8b9cb21e38c443b160e018a3422e4056eba700d1))

## [1.5.2](https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers/compare/v1.5.1...v1.5.2) (2026-09-19)


### Bug Fixes

* stop reporting ZA0209 where no boxing occurs, and catch the enum cases it hid ([#59](https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers/issues/59)) ([22040cd](https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers/commit/22040cd292e801c5ff2790da26766fe4daf9ca23))

## [1.5.1](https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers/compare/v1.5.0...v1.5.1) (2026-09-19)


### Bug Fixes

* **ci:** stamp the assembly version when publishing from a manifest ([#57](https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers/issues/57)) ([b7291fd](https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers/commit/b7291fdaea57ea7eb783df8a7883ab5ea33cb4f8))

## [1.5.0](https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers/compare/v1.4.0...v1.5.0) (2026-06-27)


### Features

* add Native AOT compatibility analyzers (ZA17xx) ([#28](https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers/issues/28)) ([10ae28b](https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers/commit/10ae28b1e1bfd7e31ea8a01bf1e72a44f67ef67a))

## [1.4.0](https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers/compare/v1.3.15...v1.4.0) (2026-06-27)


### Features

* add cache-line data-layout analyzers (ZA16xx) ([#26](https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers/issues/26)) ([a7d4f75](https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers/commit/a7d4f75c0dc8b398934fd5806f14026e84e8dd27))
