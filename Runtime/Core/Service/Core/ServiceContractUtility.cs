using System;

namespace AlicizaX
{
    internal static class ServiceContractUtility
    {
        public static ServiceContracts Create(Type serviceType)
            => new ServiceContracts(serviceType, null, null, true);

        public static ServiceContracts Create(Type serviceType, Type contractType)
            => new ServiceContracts(serviceType, contractType, null, true);

        public static ServiceContracts Create(Type serviceType, Type contractType, Type extraContractType)
            => new ServiceContracts(serviceType, contractType, extraContractType, true);

        public static ServiceContracts CreateExplicit(Type contractType, Type extraContractType)
            => new ServiceContracts(null, contractType, extraContractType, false);
    }

    internal readonly struct ServiceContracts
    {
        private readonly Type _serviceType;
        private readonly Type _contractType;
        private readonly Type _extraContractType;
        private readonly bool _includeServiceType;

        public ServiceContracts(Type serviceType, Type contractType, Type extraContractType, bool includeServiceType)
        {
            _serviceType = serviceType;
            _contractType = contractType;
            _extraContractType = extraContractType;
            _includeServiceType = includeServiceType;
        }

        public int Count
        {
            get
            {
                int count = _includeServiceType && _serviceType != null ? 1 : 0;
                if (_contractType != null && (!_includeServiceType || _contractType != _serviceType))
                {
                    count++;
                }

                if (_extraContractType != null &&
                    (!_includeServiceType || _extraContractType != _serviceType) &&
                    _extraContractType != _contractType)
                {
                    count++;
                }

                return count;
            }
        }

        public Type this[int index]
        {
            get
            {
                int current = 0;
                if (_includeServiceType && _serviceType != null)
                {
                    if (index == current) return _serviceType;
                    current++;
                }

                if (_contractType != null && (!_includeServiceType || _contractType != _serviceType))
                {
                    if (index == current) return _contractType;
                    current++;
                }

                if (_extraContractType != null &&
                    (!_includeServiceType || _extraContractType != _serviceType) &&
                    _extraContractType != _contractType)
                {
                    if (index == current) return _extraContractType;
                }

                throw new IndexOutOfRangeException();
            }
        }
    }
}
