// Adapted from Oct Prometheus M40a (GPL-3.0); see Prometheus-NOTICE.txt.
namespace __sdslv_cooperative {
enum Scope { ScopeWorkgroup = 2, ScopeSubgroup = 3 };
enum Layout { RowMajor = 0, ColumnMajor = 1 };

using MatrixAType = vk::SpirvOpaqueType<
    4456, float16_t, vk::integral_constant<uint, ScopeSubgroup>,
    vk::integral_constant<uint, 16>, vk::integral_constant<uint, 16>,
    vk::integral_constant<uint, 0> >;
using MatrixBType = vk::SpirvOpaqueType<
    4456, float16_t, vk::integral_constant<uint, ScopeSubgroup>,
    vk::integral_constant<uint, 16>, vk::integral_constant<uint, 16>,
    vk::integral_constant<uint, 1> >;
using MatrixCType = vk::SpirvOpaqueType<
    4456, float, vk::integral_constant<uint, ScopeSubgroup>,
    vk::integral_constant<uint, 16>, vk::integral_constant<uint, 16>,
    vk::integral_constant<uint, 2> >;

struct MatrixA {
  [[vk::ext_extension("SPV_KHR_cooperative_matrix")]]
  [[vk::ext_capability(6022)]]
  [[vk::ext_capability(5345)]]
  MatrixAType value;
};
struct MatrixB { MatrixBType value; };
struct MatrixC { MatrixCType value; };

template <typename PointeeType>
using WorkgroupPointer = const vk::SpirvOpaqueType<
    32, vk::Literal<vk::integral_constant<uint, ScopeWorkgroup> >, PointeeType>;

template <typename T>
[[vk::ext_instruction(83)]] WorkgroupPointer<T>
GetGroupSharedAddress([[vk::ext_reference]] T value);

template <typename ResultType, typename ComponentType>
[[vk::ext_instruction(80)]] ResultType Splat(ComponentType value);

template <typename ResultType, typename PointerType>
[[vk::ext_instruction(4457)]] ResultType Load(
    WorkgroupPointer<PointerType> pointer, Layout layout, uint stride,
    [[vk::ext_literal]] uint memoryOperands, Scope scope);

template <typename ResultType, typename MatrixTypeA, typename MatrixTypeB,
          typename MatrixTypeC>
[[vk::ext_instruction(4459)]] ResultType MultiplyAdd(
    MatrixTypeA a, MatrixTypeB b, MatrixTypeC c,
    [[vk::ext_literal]] uint operands);

template <typename ObjectType, typename PointerType>
[[vk::ext_instruction(4458)]] void Store(
    [[vk::ext_reference]] PointerType pointer, ObjectType object, Layout layout,
    uint stride, [[vk::ext_literal]] uint memoryOperands);
}

groupshared float16_t __sdslv_cooperative_tile_a[256];
groupshared float16_t __sdslv_cooperative_tile_b[256];

